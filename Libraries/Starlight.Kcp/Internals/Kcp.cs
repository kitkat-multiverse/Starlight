using System.IO.Hashing;
using static Starlight.Kcp.Internals.KcpConstants;

namespace Starlight.Kcp.Internals;

public sealed class KCP
{
    public KCP(int conv, int token, bool stream, IWriter sink)
    {
        Conv = conv;
        Token = token;
        Stream = stream;
        Output = sink;
        Mss = Mtu - KCP_OVERHEAD;
    }

    /// <summary>Conversation ID.</summary>
    public int Conv { get; set; }

    /// <summary>Maximum Transmission Unit.</summary>
    public int Mtu { get; set; } = KCP_MTU_DEF;

    /// <summary>Maximum Segment Size.</summary>
    public int Mss { get; set; } = KCP_MTU_DEF - KCP_OVERHEAD;

    /// <summary>Connection state.</summary>
    public int State { get; set; }

    /// <summary>User token.</summary>
    public int Token { get; }

    /// <summary>First unacknowledged packet.</summary>
    public int SndUna { get; set; }

    /// <summary>Next packet.</summary>
    public int SndNxt { get; set; }

    /// <summary>Next packet to be received.</summary>
    public int RcvNxt { get; set; }

    /// <summary>Congestion window threshold.</summary>
    public int Ssthresh { get; set; } = KCP_THRESH_INIT;

    /// <summary>ACK receive variable RTT.</summary>
    public long RxRttval { get; set; }

    /// <summary>ACK receive smoothed RTT.</summary>
    public long RxSrtt { get; set; }

    /// <summary>Resend timeout calculated from ACK delay.</summary>
    public long RxRto { get; set; } = KCP_RTO_DEF;

    /// <summary>Minimal resend timeout.</summary>
    public int RxMinrto { get; set; } = KCP_RTO_MIN;

    /// <summary>Send window.</summary>
    public int SndWnd { get; set; } = KCP_WND_SND;

    /// <summary>Receive window.</summary>
    public int RcvWnd { get; set; } = KCP_WND_RCV;

    /// <summary>Remote receive window.</summary>
    public int RmtWnd { get; set; } = KCP_WND_RCV;

    /// <summary>Congestion window.</summary>
    public int Cwnd { get; set; }

    /// <summary>Window probe flags.</summary>
    public int Probe { get; set; }

    /// <summary>Last update time.</summary>
    public long Current { get; set; }

    /// <summary>Flush interval.</summary>
    public int Interval { get; set; } = KCP_INTERVAL;

    /// <summary>Next flush timestamp.</summary>
    public long TsFlush { get; set; } = KCP_INTERVAL;

    public int Xmit { get; set; }
    public bool Nodelay { get; set; }
    public bool Updated { get; set; }
    public int TsProbe { get; set; }
    public int ProbeWait { get; set; }
    public int DeadLink { get; set; } = KCP_DEADLINK;
    public int Incr { get; set; }

    public List<KcpSegment> SndQueue { get; } = new();
    public List<KcpSegment> RcvQueue { get; } = new();
    public List<KcpSegment> SndBuf { get; } = new();
    public List<KcpSegment> RcvBuf { get; } = new();

    /// <summary>Pending ACKs: SN and timestamp.</summary>
    public List<(int Sn, int Ts)> AckList { get; } = new();

    public ByteBuffer Buffer { get; } = new(new byte[(KCP_MTU_DEF + KCP_OVERHEAD_HYV_V1) * 3]);

    /// <summary>ACK number to trigger fast resend.</summary>
    public int FastResend { get; set; }

    /// <summary>Disable congestion control.</summary>
    public bool NoCwnd { get; set; }

    /// <summary>Enable stream mode.</summary>
    public bool Stream { get; }

    /// <summary>Get conv from the next input call.</summary>
    public bool InputConv { get; set; }

    public IWriter Output { get; set; }
    public KcpVersion KcpVersion { get; set; } = KcpVersion.KCP_UNKNOWN;

    public void SetNodelay(bool nodelay, int interval, int resend, bool nc)
    {
        Nodelay = nodelay;
        RxMinrto = nodelay ? KCP_RTO_NDL : KCP_RTO_MIN;

        Interval = interval switch {
            < 10 => 10,
            > 5000 => 5000,
            _ => interval
        };

        if (resend >= 0)
        {
            FastResend = resend;
        }

        NoCwnd = nc;
    }

    public void SetWindowSize(int sndwnd, int rcvwnd)
    {
        if (sndwnd > 0)
        {
            SndWnd = sndwnd;
        }

        if (rcvwnd > 0)
        {
            RcvWnd = Math.Max(rcvwnd, KCP_WND_RCV);
        }
    }

    public KcpResult<bool> Update(long current)
    {
        Current = current;

        if (!Updated)
        {
            Updated = true;
            TsFlush = Current;
        }

        var slap = TimeDiff(Current, TsFlush);

        if (slap >= 10000 || slap < -10000)
        {
            TsFlush = Current;
            slap = 0;
        }

        if (slap >= 0)
        {
            TsFlush += Interval;

            if (TimeDiff(Current, TsFlush) >= 0)
            {
                TsFlush = Current + Interval;
            }

            var flush = Flush();

            if (flush.IsFailure)
            {
                return KcpResult<bool>.Failure(flush.Exception!);
            }
        }

        return KcpResult<bool>.Success(true);
    }

    private static long TimeDiff(long later, long earlier) => later - earlier;
    private static int TimeDiff(int later, int earlier) => later - earlier;

    private void ParseUna(long una)
    {
        while (SndBuf.Count > 0)
        {
            if (TimeDiff(una, SndBuf[0].Sn) > 0)
            {
                SndBuf.RemoveAt(0);
            } else
            {
                break;
            }
        }
    }

    private void ShrinkBuf()
    {
        SndUna = SndBuf.Count > 0 ? SndBuf[0].Sn : SndNxt;
    }

    private static long Bound(long lower, long value, long upper) => Math.Min(Math.Max(lower, value), upper);

    private void UpdateAck(long rtt)
    {
        if (RxSrtt == 0)
        {
            RxSrtt = rtt;
            RxRttval = rtt / 2;
        } else
        {
            var delta = rtt > RxSrtt ? rtt - RxSrtt : RxSrtt - rtt;
            RxRttval = (3 * RxRttval + delta) / 4;
            RxSrtt = (7 * RxSrtt + rtt) / 8;

            if (RxSrtt < 1)
            {
                RxSrtt = 1;
            }
        }

        var rto = RxSrtt + Math.Max(Interval, 4 * RxRttval);
        RxRto = Bound(RxMinrto, rto, KCP_RTO_MAX);
    }

    private void ParseAck(int sn)
    {
        if (TimeDiff(sn, SndUna) < 0 || TimeDiff(sn, SndNxt) >= 0)
        {
            return;
        }

        for (var i = 0; i < SndBuf.Count; i++)
        {
            var buffSn = SndBuf[i].Sn;

            if (buffSn == sn)
            {
                SndBuf.RemoveAt(i);
                break;
            }

            if (buffSn > sn)
            {
                break;
            }
        }
    }

    private void AckPush(int sn, int ts)
    {
        AckList.Add((sn, ts));
    }

    private void ParseData(KcpSegment newSegment)
    {
        var sn = newSegment.Sn;

        if (TimeDiff(sn, RcvNxt + RcvWnd) >= 0 || TimeDiff(sn, RcvNxt) < 0)
        {
            return;
        }

        var repeat = false;
        var newIndex = RcvBuf.Count;

        foreach (var item in RcvBuf)
        {
            if (item.Sn == sn)
            {
                repeat = true;
                break;
            }

            if (TimeDiff(sn, item.Sn) > 0)
            {
                continue;
            }

            newIndex -= 1;
        }

        if (!repeat)
        {
            RcvBuf.Insert(newIndex, newSegment);
        }

        MoveBuf();
    }

    private void MoveBuf()
    {
        while (RcvBuf.Count > 0)
        {
            var nrcvQue = RcvQueue.Count;
            var seg = RcvBuf[0];

            if (seg.Sn == RcvNxt && nrcvQue < RcvWnd)
            {
                RcvNxt += 1;
            } else
            {
                break;
            }

            RcvBuf.RemoveAt(0);
            RcvQueue.Add(seg);
        }
    }

    private void ParseFastAck(int sn)
    {
        if (TimeDiff(sn, SndUna) < 0 || TimeDiff(sn, SndNxt) >= 0)
        {
            return;
        }

        foreach (var segment in SndBuf)
        {
            if (TimeDiff(sn, segment.Sn) < 0)
            {
                continue;
            }

            if (sn != segment.Sn)
            {
                segment.FastAck += 1;
            }
        }
    }

    private KcpVersion DetermineKcpVersion(ByteCursor cursor, int dataLen)
    {
        var remainingAfterData = cursor.Remaining - dataLen;

        return remainingAfterData switch {
            KCP_EXTRA_OVERHEAD_DEFAULT => KcpVersion.KCP_BASE,
            KCP_EXTRA_OVERHEAD_HYV_V1 => KcpVersion.KCP_HYV_V1,
            _ when remainingAfterData < KcpVersion.KCP_BASE.Overhead() => KcpVersion.KCP_UNKNOWN,
            _ when cursor.Peek32LE(dataLen + KCP_EXTRA_OVERHEAD_HYV_V1) == Conv => KcpVersion.KCP_HYV_V1,
            _ when cursor.Peek32LE(dataLen + KCP_EXTRA_OVERHEAD_DEFAULT) == Conv => KcpVersion.KCP_BASE,
            _ => KcpVersion.KCP_UNKNOWN
        };
    }

    public KcpResult<long> Input(ByteCursor data)
    {
        var inputSize = data.Size;

        if (inputSize < KcpVersion.Overhead())
        {
            return KcpResult<long>.Failure(new InvalidOperationException($"Error::InvalidSegmentSize({inputSize})"));
        }

        var flag = false;
        var maxAck = 0;
        var oldUna = SndUna;

        while (data.Remaining >= KcpVersion.Overhead())
        {
            var conv = data.Read32LE();

            if (conv != Conv)
            {
                if (InputConv)
                {
                    Conv = conv;
                    InputConv = false;
                } else
                {
                    return KcpResult<long>.Failure(new InvalidOperationException($"Error::ConvInconsistent({Conv}, {conv})"));
                }
            }

            var token = data.Read32LE();
            var cmd = data.Read8();
            var frg = data.Read8U();
            var wnd = data.Read16LE();
            var ts = data.Read32LE();
            var sn = data.Read32LE();
            var una = data.Read32LE();
            var len = data.Read32LE();

            if (data.Remaining < len)
            {
                return KcpResult<long>.Failure(new InvalidOperationException($"Error::InvalidSegmentDataSize({len}, {data.Remaining})"));
            }

            if (KcpVersion == KcpVersion.KCP_UNKNOWN)
            {
                KcpVersion = DetermineKcpVersion(data, len);
            }
            Console.WriteLine($"Determined KCP version: {KcpVersion}");

            if (KcpVersion == KcpVersion.KCP_UNKNOWN)
            {
                return KcpResult<long>.Failure(new InvalidOperationException("Error::UnknownKcpVersion"));
            }

            int? byteCheckCode = KcpVersion.HasExtraHash() ? data.Read32LE() : null;

            if (cmd != KCP_CMD_PUSH && cmd != KCP_CMD_ACK && cmd != KCP_CMD_WASK && cmd != KCP_CMD_WINS)
            {
                return KcpResult<long>.Failure(new InvalidOperationException($"Error::UnsupportedCmd({cmd})"));
            }

            if (token != Token)
            {
                return KcpResult<long>.Failure(new InvalidOperationException($"Error::TokenMismatch({token}, {Token})"));
            }

            RmtWnd = wnd;
            ParseUna(una);
            ShrinkBuf();

            var hasReadData = false;

            switch (cmd)
            {
                case KCP_CMD_ACK: {
                    var rtt = TimeDiff(Current, ts);

                    if (rtt >= 0)
                    {
                        UpdateAck(rtt);
                    }

                    ParseAck(sn);
                    ShrinkBuf();

                    if (!flag)
                    {
                        maxAck = sn;
                        flag = true;
                    } else if (TimeDiff(sn, maxAck) > 0)
                    {
                        maxAck = sn;
                    }

                    break;
                }

                case KCP_CMD_PUSH: {
                    if (TimeDiff(sn, RcvNxt + RcvWnd) < 0)
                    {
                        AckPush(sn, ts);

                        if (TimeDiff(sn, RcvNxt) >= 0)
                        {
                            var sbuf = data.Read(len);
                            hasReadData = true;

                            var segment = new KcpSegment(sbuf, KcpVersion) {
                                Conv = conv,
                                Token = token,
                                Cmd = cmd,
                                Frg = frg,
                                Wnd = wnd,
                                Ts = ts,
                                Sn = sn,
                                Una = una,
                                ByteCheckCode = byteCheckCode
                            };

                            ParseData(segment);
                        }
                    }

                    break;
                }

                case KCP_CMD_WASK:
                    Probe |= KCP_ASK_TELL;
                    break;

                case KCP_CMD_WINS:
                    break;
            }

            if (!hasReadData)
            {
                var nextPos = data.BytesRead + len;
                data.SetBytesRead(nextPos);
            }
        }

        if (flag)
        {
            ParseFastAck(maxAck);
        }

        if (SndUna > oldUna && Cwnd < RmtWnd)
        {
            var mss = Mss;

            if (Cwnd < Ssthresh)
            {
                Cwnd += 1;
                Incr += mss;
            } else
            {
                if (Incr < mss)
                {
                    Incr = mss;
                }

                Incr += mss * mss / Incr + mss / 16;

                if ((Cwnd + 1) * mss <= Incr)
                {
                    Cwnd += 1;
                }
            }

            if (Cwnd > RmtWnd)
            {
                Cwnd = RmtWnd;
                Incr = RmtWnd * mss;
            }
        }

        return KcpResult<long>.Success(data.BytesRead);
    }

    public int WndUnused() => RcvQueue.Count < RcvWnd ? RcvWnd - RcvQueue.Count : 0;

    private KcpResult<bool> FlushAck(KcpSegment segment)
    {
        foreach (var (sn, ts) in AckList)
        {
            if (Buffer.BytesWritten + KcpVersion.Overhead() > Mtu)
            {
                Output.Write(Buffer.GetWrittenBytes());
                Buffer.Clear();
            }

            segment.Sn = sn;
            segment.Ts = ts;
            segment.Encode(Buffer);
        }

        AckList.Clear();
        return KcpResult<bool>.Success(true);
    }

    private void ProbeWndSize()
    {
        if (RmtWnd == 0)
        {
            if (ProbeWait == 0)
            {
                ProbeWait = KCP_PROBE_INIT;
                TsProbe = unchecked((int)(Current + ProbeWait));
            } else
            {
                if (TimeDiff(Current, TsProbe) >= 0 && ProbeWait < KCP_PROBE_INIT)
                {
                    ProbeWait = KCP_PROBE_INIT;
                }

                ProbeWait += ProbeWait / 2;

                if (ProbeWait > KCP_PROBE_LIMIT)
                {
                    ProbeWait = KCP_PROBE_LIMIT;
                }

                TsProbe = unchecked((int)(Current + ProbeWait));
                Probe |= KCP_ASK_SEND;
            }
        } else
        {
            TsProbe = 0;
            ProbeWait = 0;
        }
    }

    private KcpResult<bool> FlushProbeCommand(byte cmd, KcpSegment segment)
    {
        segment.Cmd = cmd;

        if (Buffer.BytesWritten + KcpVersion.Overhead() > Mtu)
        {
            Output.Write(Buffer.GetWrittenBytes());
            Buffer.Clear();
        }

        segment.Encode(Buffer);
        return KcpResult<bool>.Success(true);
    }

    private KcpResult<bool> FlushProbeCommands(KcpSegment segment)
    {
        if ((Probe & KCP_ASK_SEND) != 0)
        {
            var result = FlushProbeCommand(KCP_CMD_WASK, segment);

            if (result.IsFailure)
            {
                return result;
            }
        }

        if ((Probe & KCP_ASK_TELL) != 0)
        {
            var result = FlushProbeCommand(KCP_CMD_WINS, segment);

            if (result.IsFailure)
            {
                return result;
            }
        }

        Probe = 0;
        return KcpResult<bool>.Success(true);
    }

    /// <summary>Flushes pending data in the internal buffer.</summary>
    public KcpResult<bool> Flush()
    {
        if (!Updated)
        {
            return KcpResult<bool>.Failure(new InvalidOperationException("Error::NeedUpdate"));
        }

        var segment = new KcpSegment(data: null, KcpVersion) {
            Conv = Conv,
            Token = Token,
            Cmd = KCP_CMD_ACK,
            Wnd = WndUnused(),
            Una = RcvNxt
        };

        var ack = FlushAck(segment);

        if (ack.IsFailure)
        {
            return ack;
        }

        ProbeWndSize();

        var probe = FlushProbeCommands(segment);

        if (probe.IsFailure)
        {
            return probe;
        }

        var cwnd = Math.Min(SndWnd, RmtWnd);

        if (!NoCwnd)
        {
            cwnd = Math.Min(Cwnd, cwnd);
        }

        while (TimeDiff(SndNxt, SndUna + cwnd) < 0)
        {
            if (SndQueue.Count == 0)
            {
                break;
            }

            var newSegment = SndQueue[0];
            SndQueue.RemoveAt(0);

            newSegment.Conv = Conv;
            newSegment.Token = Token;
            newSegment.Cmd = KCP_CMD_PUSH;
            newSegment.Wnd = segment.Wnd;
            newSegment.Ts = unchecked((int)Current);
            newSegment.Sn = SndNxt;
            SndNxt += 1;
            newSegment.Una = RcvNxt;
            newSegment.ResendTs = unchecked((int)Current);
            newSegment.Rto = unchecked((int)RxRto);
            newSegment.FastAck = 0;
            newSegment.Xmit = 0;

            if (KcpVersion.HasExtraHash())
            {
                newSegment.ByteCheckCode = ComputeByteCheckCode(newSegment.Data ?? Array.Empty<byte>());
            }

            SndBuf.Add(newSegment);
        }

        var resent = FastResend > 0 ? FastResend : int.MaxValue;
        var rtomin = !Nodelay ? RxRto << 3 : 0;

        var lost = false;
        var change = 0;

        foreach (var sndSegment in SndBuf)
        {
            var needSend = false;

            if (sndSegment.Xmit == 0)
            {
                needSend = true;
                sndSegment.Xmit += 1;
                sndSegment.Rto = unchecked((int)RxRto);
                sndSegment.ResendTs = unchecked((int)(Current + sndSegment.Rto + rtomin));
            } else if (TimeDiff(Current, sndSegment.ResendTs) >= 0)
            {
                needSend = true;
                sndSegment.Xmit += 1;
                Xmit += 1;

                if (!Nodelay)
                {
                    sndSegment.Rto += unchecked((int)RxRto);
                } else
                {
                    sndSegment.Rto += unchecked((int)(RxRto / 2));
                }

                sndSegment.ResendTs = unchecked((int)(Current + sndSegment.Rto));
                lost = true;
            } else if (sndSegment.FastAck >= resent)
            {
                needSend = true;
                sndSegment.Xmit += 1;
                sndSegment.FastAck = 0;
                sndSegment.ResendTs = unchecked((int)(Current + sndSegment.Rto));
                change += 1;
            }

            if (needSend)
            {
                sndSegment.Ts = unchecked((int)Current);
                sndSegment.Wnd = segment.Wnd;
                sndSegment.Una = RcvNxt;

                var need = KcpVersion.Overhead() + (sndSegment.Data?.Length ?? 0);

                if (Buffer.BytesWritten + need > Mtu)
                {
                    Output.Write(Buffer.GetWrittenBytes());
                    Buffer.Clear();
                }

                sndSegment.Encode(Buffer);

                if (sndSegment.Xmit >= DeadLink)
                {
                    State = -1;
                }
            }
        }

        if (!Buffer.IsEmpty)
        {
            Output.Write(Buffer.GetWrittenBytes());
            Buffer.Clear();
        }

        if (change > 0)
        {
            var inflight = SndNxt - SndUna;
            Ssthresh = inflight / 2;

            if (Ssthresh < KCP_THRESH_MIN)
            {
                Ssthresh = KCP_THRESH_MIN;
            }

            Cwnd = Ssthresh + resent;
            Incr = Cwnd * Mss;
        }

        if (lost)
        {
            Ssthresh = cwnd / 2;

            if (Ssthresh < KCP_THRESH_MIN)
            {
                Ssthresh = KCP_THRESH_MIN;
            }

            Cwnd = 1;
            Incr = Mss;
        }

        if (Cwnd < 1)
        {
            Cwnd = 1;
            Incr = Mss;
        }

        return KcpResult<bool>.Success(true);
    }

    public KcpResult<int> PeekSize()
    {
        if (RcvQueue.Count == 0)
        {
            return KcpResult<int>.Failure(new InvalidOperationException("Error::RecvQueueEmpty"));
        }

        var segment = RcvQueue[0];

        if (segment.Frg == 0)
        {
            return KcpResult<int>.Success(segment.Data?.Length ?? 0);
        }

        if (RcvQueue.Count < segment.Frg + 1)
        {
            return KcpResult<int>.Failure(new InvalidOperationException("Error::ExpectingFragment"));
        }

        var len = 0;

        foreach (var item in RcvQueue)
        {
            len += item.Data?.Length ?? 0;

            if (item.Frg == 0)
            {
                break;
            }
        }

        return KcpResult<int>.Success(len);
    }

    public KcpResult<int> Recv(byte[] destination)
    {
        if (RcvQueue.Count == 0)
        {
            return KcpResult<int>.Failure(new InvalidOperationException("Error::RecvQueueEmpty"));
        }

        var peek = PeekSize();

        if (peek.IsFailure)
        {
            return KcpResult<int>.Failure(peek.Exception!);
        }

        var peeksize = peek.Value;

        if (peeksize > destination.Length)
        {
            return KcpResult<int>.Failure(new InvalidOperationException("Error::UserBufTooSmall"));
        }

        var recover = RcvQueue.Count >= RcvWnd;
        var cur = new ByteBuffer(destination);

        while (RcvQueue.Count > 0)
        {
            var seg = RcvQueue[0];
            RcvQueue.RemoveAt(0);
            cur.Write(seg.Data ?? Array.Empty<byte>());

            if (seg.Frg == 0)
            {
                break;
            }
        }

        if (cur.BytesWritten != peeksize)
        {
            return KcpResult<int>.Failure(new InvalidOperationException("Error::PeekSizeWrittenMismatch"));
        }

        MoveBuf();

        if (RcvQueue.Count < RcvWnd && recover)
        {
            Probe |= KCP_ASK_TELL;
        }

        return KcpResult<int>.Success(cur.BytesWritten);
    }

    public KcpResult<bool> Send(byte[] data)
    {
        if (data.Length == 0)
            return KcpResult<bool>.Failure(new InvalidOperationException("Error::EmptyData"));

        var count = Stream ? (data.Length + Mss - 1) / Mss
            : data.Length <= Mss ? 1 : (data.Length + Mss - 1) / Mss;

        if (count >= KCP_WND_RCV)
            return KcpResult<bool>.Failure(new InvalidOperationException("Error::DataTooLarge"));

        var offset = 0;

        for (var i = 0; i < count; i++)
        {
            var size = Math.Min(data.Length - offset, Mss);
            var buf = new byte[size];
            Array.Copy(data, offset, buf, destinationIndex: 0, size);
            offset += size;

            var segment = new KcpSegment(buf, KcpVersion) {
                Frg = (byte)(Stream ? 0 : count - i - 1)
            };

            SndQueue.Add(segment);
        }

        return KcpResult<bool>.Success(true);
    }

    private static int ComputeByteCheckCode(byte[] data)
    {
        var hash = XxHash3.HashToUInt64(data);
        return unchecked((int)(hash & 0xFFFFFFFFUL));
    }
}
