using Vigie.Core.Interop;

namespace Vigie.Core.Platform;

public sealed class SystemClock : IClock
{
    public long TickCountMilliseconds => Environment.TickCount64;

    public long UnbiasedMilliseconds
    {
        get
        {
            if (NativeMethods.QueryUnbiasedInterruptTime(out ulong hundredNanoseconds))
            {
                return (long)(hundredNanoseconds / 10_000UL);
            }

            // The call has no documented failure mode on supported systems; fall
            // back to the biased counter so a reading is always available.
            return Environment.TickCount64;
        }
    }

    public DateTimeOffset Now => DateTimeOffset.Now;
}
