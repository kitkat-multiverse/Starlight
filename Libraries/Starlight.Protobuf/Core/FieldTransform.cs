namespace Starlight.Protobuf.Core;

/// <summary>
/// An invertible integer transform applied to a single scalar field's value on the
/// wire (the obfuscation expressed by proto <c>add</c>/<c>xor</c>/<c>fop</c>/<c>mask</c>
/// options). <see cref="Encode"/> maps the real value to the wire value; <see cref="Decode"/>
/// inverts it. Used only by the reflective slow path -- the generated fast path inlines
/// the same arithmetic. All math is modular (<c>unchecked</c> over <see cref="long"/>).
/// </summary>
public sealed class FieldTransform
{
    private readonly string _ops;
    private readonly long[] _operands;

    /// <param name="ops">Encode op chain; one char per step in <c>{ '+', '-', '^' }</c>.</param>
    /// <param name="operands">Operand for each op, positionally paired with <paramref name="ops"/>.</param>
    public FieldTransform(string ops, long[] operands)
    {
        _ops = ops;
        _operands = operands;
    }

    /// <summary>Real value -> wire value: applies each op left to right.</summary>
    public long Encode(long value)
    {
        unchecked
        {
            for (var i = 0; i < _ops.Length; i++)
                value = Apply(_ops[i], value, _operands[i]);
            return value;
        }
    }

    /// <summary>Wire value -> real value: applies the inverse of each op in reverse order.</summary>
    public long Decode(long value)
    {
        unchecked
        {
            for (var i = _ops.Length - 1; i >= 0; i--)
                value = Apply(Inverse(_ops[i]), value, _operands[i]);
            return value;
        }
    }

    private static long Apply(char op, long value, long operand) => op switch {
        '+' => value + operand,
        '-' => value - operand,
        _ => value ^ operand
    };

    private static char Inverse(char op) => op switch { '+' => '-', '-' => '+', _ => '^' };
}
