using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Google.Protobuf.Reflection;
using FType = Google.Protobuf.Reflection.FieldDescriptorProto.Type;

namespace Starlight.Protobuf.Compiler;

internal static partial class CodeEmitter
{
    // ---- field value transforms (add / xor / fop / mask) --------------------

    /// <summary>
    /// An invertible integer transform applied to a field on the wire. <see cref="Ops"/>
    /// is the encode chain (real -&gt; wire), one char per step in <c>{ '+', '-', '^' }</c>,
    /// paired positionally with <see cref="Operands"/>. Decode (wire -&gt; real) applies the
    /// inverse of each op in reverse order. A <c>mask</c> that <see cref="ParseMask"/> can't
    /// invert is rejected outright (no transform) and reported as an error, so every
    /// transform that survives here round-trips on both the fast and reflective paths.
    /// </summary>
    internal sealed class Transform
    {
        public string Ops = "";
        public long[] Operands = [];
    }

    /// <summary>A mask rejected at compile time because it cannot be inverted for decode.</summary>
    internal sealed class MaskViolation
    {
        public string Message = "";
        public string Field = "";
        public string Mask = "";

        /// <summary>True = out-of-grammar (SLPB005). False = in-grammar but structurally non-invertible (SLPB006). Both are errors.</summary>
        public bool Invalid;
    }

    /// <summary>Per-message field transform lookup, keyed by message name then field (proto) name.</summary>
    internal sealed class TransformTable
    {
        private readonly Dictionary<string, Dictionary<string, Transform>> _map;

        public TransformTable(Dictionary<string, Dictionary<string, Transform>> map, IReadOnlyList<MaskViolation> violations)
        {
            _map = map;
            Violations = violations;
        }

        public IReadOnlyList<MaskViolation> Violations { get; }

        public Transform? Get(string message, string field) =>
            _map.TryGetValue(message, out var fields) && fields.TryGetValue(field, out var t) ? t : null;
    }

    // ---- alternate field names (alts) ---------------------------------------

    /// <summary>
    /// Per-message alternate-name lookup, keyed by message name then base field name. The
    /// canonical (base) proto declares <c>[alts = "..."]</c> on a field to list the version
    /// field names that should correlate to it, so a version may rename a field without
    /// breaking the base&lt;-&gt;version match. Authored on base protos only.
    /// </summary>
    internal sealed class AltsTable
    {
        private readonly Dictionary<string, Dictionary<string, List<string>>> _map;

        public AltsTable(Dictionary<string, Dictionary<string, List<string>>> map)
        {
            _map = map;
        }

        public IReadOnlyList<string> Get(string message, string field) =>
            _map.TryGetValue(message, out var fields) && fields.TryGetValue(field, out var a) ? a : System.Array.Empty<string>();
    }

    /// <summary>Reads the repeated <c>alts</c> field option off every message in the set into an <see cref="AltsTable"/>.</summary>
    public static AltsTable ReadAlts(FileDescriptorSet set)
    {
        var map = new Dictionary<string, Dictionary<string, List<string>>>();

        foreach (var file in set.Files)
        foreach (var msg in file.MessageTypes)
        {
            ReadMessageAlts(msg, map);
        }
        return new AltsTable(map);
    }

    private static void ReadMessageAlts(DescriptorProto msg, Dictionary<string, Dictionary<string, List<string>>> map)
    {
        foreach (var field in msg.Fields)
        {
            var alts = ReadFieldAlts(field.Options);
            if (alts.Count == 0) continue;

            if (!map.TryGetValue(msg.Name, out var fields))
                map[msg.Name] = fields = new Dictionary<string, List<string>>();
            fields[field.Name] = alts;
        }

        foreach (var nested in msg.NestedTypes)
        {
            ReadMessageAlts(nested, map);
        }
    }

    private static List<string> ReadFieldAlts(FieldOptions? options)
    {
        var alts = new List<string>();
        if (options is null) return alts;

        // `alts` is a repeated extension protobuf-net can't resolve, so each value is parked
        // as a single-part uninterpreted option carrying the (unquoted) string literal.
        foreach (var opt in options.UninterpretedOptions)
        {
            if (opt.Names.Count != 1 || opt.Names[0].name_part != "alts") continue;

            var value = opt.AggregateValue ?? "";
            if (value.Length != 0) alts.Add(value);
        }

        return alts;
    }

    /// <summary>
    /// The version field correlated to a base field: matched by prefix-stripped name, or --
    /// failing that -- by any name the base field lists in its <c>alts</c> option (also
    /// stripped). The '_' custom-name marker is stripped on both sides, mirroring
    /// <see cref="FieldsByName"/>. Returns null when the version has no matching field (the
    /// field is then not serialized for that version).
    /// </summary>
    internal static FieldDescriptorProto? MatchVersionField(
        FieldDescriptorProto baseField,
        string baseMsgName,
        Dictionary<string, FieldDescriptorProto> versionByName,
        AltsTable? alts
    )
    {
        if (versionByName.TryGetValue(StripPrefix(baseField.Name), out var direct)) return direct;

        if (alts is not null)
            foreach (var alt in alts.Get(baseMsgName, baseField.Name))
            {
                if (versionByName.TryGetValue(StripPrefix(alt), out var matched)) return matched;
            }
        return null;
    }

    /// <summary>Integer kinds the transforms apply to. Floats, bools, strings, enums and messages are excluded.</summary>
    internal static bool IsTransformable(FType type) => type switch {
        FType.TypeInt32 or FType.TypeInt64 or FType.TypeUint32 or FType.TypeUint64
            or FType.TypeSint32 or FType.TypeSint64 or FType.TypeFixed32 or FType.TypeFixed64
            or FType.TypeSfixed32 or FType.TypeSfixed64 => true,
        _ => false
    };

    // ---- codegen ------------------------------------------------------------

    /// <summary>Encode expression (real -&gt; wire) for <paramref name="valueExpr"/>, cast back to <paramref name="csType"/>.</summary>
    private static string Encode(Transform? t, string valueExpr, string csType)
    {
        if (t is null) return valueExpr;

        var inner = $"(long){valueExpr}";

        for (var i = 0; i < t.Ops.Length; i++)
            inner = $"({inner} {t.Ops[i]} {Lit(t.Operands[i])})";
        return $"unchecked(({csType})({inner}))";
    }

    /// <summary>Decode expression (wire -&gt; real) wrapping <paramref name="readExpr"/>, cast to <paramref name="csType"/>.</summary>
    private static string Decode(Transform? t, string readExpr, string csType)
    {
        if (t is null) return readExpr;

        var inner = $"(long){readExpr}";

        for (var i = t.Ops.Length - 1; i >= 0; i--)
            inner = $"({inner} {Inverse(t.Ops[i])} {Lit(t.Operands[i])})";
        return $"unchecked(({csType})({inner}))";
    }

    private static char Inverse(char op) => op switch { '+' => '-', '-' => '+', _ => '^' };

    private static string Lit(long v) => $"({v.ToString(CultureInfo.InvariantCulture)}L)";

    private static long? ParseOperand(string value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : (long?)null;

    // ---- extraction ---------------------------------------------------------

    /// <summary>
    /// Reads field transform options (add / xor / fop / mask) from a parsed descriptor set,
    /// attributing each to its declaring message through the descriptor tree (so nesting,
    /// oneofs, comments and option braces are the parser's problem, not ours). The protocol
    /// writes these as bare options (e.g. <c>[add = 5]</c>) that protobuf-net can't resolve to
    /// the extra.proto extensions, so it parks each on the field's
    /// <see cref="FieldOptions.UninterpretedOptions"/> with the literal in
    /// <see cref="UninterpretedOption.AggregateValue"/> — which is what we read here.
    /// </summary>
    public static TransformTable ReadTransforms(FileDescriptorSet set)
    {
        var map = new Dictionary<string, Dictionary<string, Transform>>();
        var violations = new List<MaskViolation>();

        foreach (var file in set.Files)
        foreach (var msg in file.MessageTypes)
        {
            ReadMessageTransforms(msg, map, violations);
        }

        return new TransformTable(map, violations);
    }

    private static void ReadMessageTransforms(
        DescriptorProto msg,
        Dictionary<string, Dictionary<string, Transform>> map,
        List<MaskViolation> violations
    )
    {
        foreach (var field in msg.Fields)
        {
            var t = BuildTransform(field.Options, msg.Name, field.Name, violations);
            if (t is null) continue;

            if (!map.TryGetValue(msg.Name, out var fields))
                map[msg.Name] = fields = new Dictionary<string, Transform>();
            fields[field.Name] = t;
        }

        foreach (var nested in msg.NestedTypes)
        {
            ReadMessageTransforms(nested, map, violations);
        }
    }

    private static Transform? BuildTransform(FieldOptions? options, string message, string field, List<MaskViolation> violations)
    {
        if (options is null) return null;

        long? add = null, xor = null;
        string? fop = null, mask = null;

        foreach (var opt in options.UninterpretedOptions)
        {
            // Our options are simple single-part names (`add`, not `foo.add`); skip anything else.
            if (opt.Names.Count != 1) continue;

            var value = opt.AggregateValue ?? "";

            switch (opt.Names[0].name_part)
            {
                case "add": add = ParseOperand(value); break;
                case "xor": xor = ParseOperand(value); break;
                case "fop": fop = value; break;
                case "mask": mask = value; break;
            }
        }

        // `mask` is the manual alternative; when present it wins over add/xor. It must be
        // invertible: the compiler derives the decode path by inverting it at build time, so
        // a mask it can't invert is rejected (no transform) rather than silently one-way.
        if (mask is not null)
        {
            var parsed = ParseMask(mask);
            if (parsed is not null) return parsed;

            // Distinguish the two failure modes purely for a clearer error: out-of-grammar
            // tokens (SLPB005) vs. legal tokens that don't form an invertible chain (SLPB006).
            violations.Add(new MaskViolation { Message = message, Field = field, Mask = mask, Invalid = !IsSafeMaskGrammar(mask) });
            return null;
        }

        if (add.HasValue && xor.HasValue)
        {
            // fop = "first operation": which of add/xor is applied first on encode.
            return fop == "xor" ?
                new Transform { Ops = "^+", Operands = [xor.Value, add.Value] } :
                new Transform { Ops = "+^", Operands = [add.Value, xor.Value] };
        }

        if (add.HasValue) return new Transform { Ops = "+", Operands = [add.Value] };
        if (xor.HasValue) return new Transform { Ops = "^", Operands = [xor.Value] };

        return null;
    }

    /// <summary>
    /// Parses a left-deep, fully-parenthesized mask such as <c>(value - 49379) ^ 11523</c>
    /// into an invertible op-chain. Returns null for anything that doesn't reduce to
    /// <c>value</c> wrapped in a chain of <c>(sub OP integer)</c> steps.
    /// </summary>
    private static Transform? ParseMask(string mask)
    {
        var ops = new StringBuilder();
        var operands = new List<long>();
        return Walk(mask) ? new Transform { Ops = ops.ToString(), Operands = operands.ToArray() } : null;

        bool Walk(string expr)
        {
            expr = expr.Trim();
            expr = StripOuterParens(expr);
            if (expr == "value") return true;

            // Find the last top-level binary operator (the outermost / last-applied op).
            var depth = 0;

            for (var i = expr.Length - 1; i > 0; i--)
            {
                var c = expr[i];

                if (c == ')') depth++;
                else if (c == '(') depth--;
                else if (depth == 0 && (c == '+' || c == '-' || c == '^'))
                {
                    var right = expr.Substring(i + 1).Trim();

                    if (!long.TryParse(right, NumberStyles.Integer, CultureInfo.InvariantCulture, out var operand))
                        return false;
                    if (!Walk(expr.Substring(startIndex: 0, i))) return false;

                    ops.Append(c);
                    operands.Add(operand);
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Strict grammar gate for a rejected mask, used only to choose the clearer diagnostic:
    /// foreign tokens (SLPB005) vs. a legal-but-non-invertible chain (SLPB006). Tokenizes the
    /// mask and verifies the whole token stream parses as a
    /// well-formed arithmetic expression over the word <c>value</c>, integer literals,
    /// parentheses, and the binary operators <c>+ - ^</c> (with optional unary <c>+ -</c>).
    /// Rejects anything else: stray identifiers, run-on tokens like <c>valuevalue</c>,
    /// adjacent operands, unbalanced parens, call syntax, punctuation.
    /// <code>
    /// expr    := unary (('+' | '-' | '^') unary)*
    /// unary   := ('+' | '-')* primary
    /// primary := 'value' | NUMBER | '(' expr ')'
    /// </code>
    /// </summary>
    private static bool IsSafeMaskGrammar(string mask)
    {
        if (!TryTokenizeMask(mask, out var tokens)) return false;

        var pos = 0;
        return ParseMaskExpr(tokens, ref pos) && pos == tokens.Count;
    }

    private static bool TryTokenizeMask(string s, out List<string> tokens)
    {
        tokens = [
        ];
        var i = 0;

        while (i < s.Length)
        {
            var c = s[i];

            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (c is '(' or ')' or '+' or '-' or '^')
            {
                tokens.Add(c.ToString());
                i++;
            } else if (char.IsDigit(c))
            {
                var start = i;
                while (i < s.Length && char.IsDigit(s[i])) i++;
                tokens.Add(s.Substring(start, i - start));
            } else if (char.IsLetter(c) || c == '_')
            {
                var start = i;
                while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++;
                // The only legal identifier is the whole word `value`; `value1`, `valuevalue`,
                // `System`, etc. are read as one run and rejected here.
                if (s.Substring(start, i - start) != "value") return false;

                tokens.Add("value");
            } else
            {
                return false;
            }
        }

        return true;
    }

    private static bool ParseMaskExpr(List<string> t, ref int p)
    {
        if (!ParseMaskUnary(t, ref p)) return false;

        while (p < t.Count && (t[p] == "+" || t[p] == "-" || t[p] == "^"))
        {
            p++;
            if (!ParseMaskUnary(t, ref p)) return false;
        }

        return true;
    }

    private static bool ParseMaskUnary(List<string> t, ref int p)
    {
        while (p < t.Count && (t[p] == "+" || t[p] == "-")) p++;
        return ParseMaskPrimary(t, ref p);
    }

    private static bool ParseMaskPrimary(List<string> t, ref int p)
    {
        if (p >= t.Count) return false;

        var tok = t[p];

        if (tok == "value" || char.IsDigit(tok[0]))
        {
            p++;
            return true;
        }

        if (tok == "(")
        {
            p++;
            if (!ParseMaskExpr(t, ref p)) return false;
            if (p >= t.Count || t[p] != ")") return false;

            p++;
            return true;
        }

        return false;
    }

    private static string StripOuterParens(string expr)
    {
        while (expr.Length >= 2 && expr[0] == '(' && expr[expr.Length - 1] == ')')
        {
            // Only strip when the leading '(' matches the trailing ')'.
            var depth = 0;
            var wraps = true;

            for (var i = 0; i < expr.Length; i++)
            {
                if (expr[i] == '(') depth++;
                else if (expr[i] == ')') depth--;

                if (depth == 0 && i < expr.Length - 1)
                {
                    wraps = false;
                    break;
                }
            }

            if (!wraps) break;

            expr = expr.Substring(startIndex: 1, expr.Length - 2).Trim();
        }

        return expr;
    }
}
