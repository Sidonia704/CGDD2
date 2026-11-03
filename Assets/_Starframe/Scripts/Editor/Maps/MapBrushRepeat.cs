using System;

namespace Starframe.EditorTools
{
    public sealed class MapBrushRepeat
    {
        public bool Held { get; private set; }
        double next, interval;
        public void Begin(double now, double delay, double repeatInterval)
        { Held = true; next = now + Math.Max(.25, delay); interval = Math.Max(.1, repeatInterval); }
        public bool TryRepeat(double now)
        {
            if (!Held || now < next) return false;
            next = now + interval; return true;
        }
        public void End() => Held = false;
    }
}
