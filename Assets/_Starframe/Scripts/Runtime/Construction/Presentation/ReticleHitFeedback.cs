// @role Coalesce confirmed hits into bounded-duration reticle feedback.

using UnityEngine;

namespace Starframe.Presentation
{
    /// <summary>Coalesces dense hit bursts without queuing delayed flashes.</summary>
    public sealed class ReticleHitFeedback
    {
        double lastHit=double.NegativeInfinity;
        public bool TryTrigger(double now,float maxRate)
        {
            if(now-lastHit<1.0/Mathf.Clamp(maxRate,1,20)-.000001)return false;
            lastHit=now;return true;
        }
        public void Reset()=>lastHit=double.NegativeInfinity;
        public void Sample(double now,float duration,out float strength,out float extension)
        {
            float t=(float)((now-lastHit)/Mathf.Max(.06f,duration));
            if(t<0||t>=1){strength=extension=0;return;}
            extension=Mathf.SmoothStep(0,1,Mathf.Clamp01(t/.32f));
            float attack=Mathf.SmoothStep(0,1,Mathf.Clamp01(t/.2f));
            float release=1-Mathf.SmoothStep(0,1,Mathf.Clamp01((t-.25f)/.75f));
            strength=attack*release;
        }
    }
}
