// @role Estimate target-core motion for projectile lead calculations.

using UnityEngine;
namespace Starframe.Combat
{
    public sealed class CoreVelocityEstimator
    {
        const int Capacity=64;
        readonly Vector3[] positions=new Vector3[Capacity];readonly double[] times=new double[Capacity];
        int start,count;
        public Vector3 Velocity{get;private set;}
        public int SampleCount=>count;
        public void Clear(){start=count=0;Velocity=Vector3.zero;}
        public void Reset(Vector3 position,double time,Vector3 initialVelocity){start=0;count=1;positions[0]=position;times[0]=time;Velocity=initialVelocity;}
        public void Sample(Vector3 position,double time,float window,float response,Vector3 fallback)
        {
            if(count==0){Reset(position,time,fallback);return;}
            int last=(start+count-1)%Capacity;double dt=time-times[last];if(dt<=.000001)return;
            if(dt>.5||(position-positions[last]).sqrMagnitude>625){Reset(position,time,fallback);return;}
            if(count==Capacity){start=(start+1)%Capacity;count--;}
            int next=(start+count)%Capacity;positions[next]=position;times[next]=time;count++;
            while(count>2&&times[start]<time-Mathf.Clamp(window,.04f,1)){start=(start+1)%Capacity;count--;}
            // Regression over several physical core positions includes translation and rotation about the ship's centre.
            double meanTime=0,denominator=0;Vector3 meanPosition=Vector3.zero,numerator=Vector3.zero,basePosition=positions[last];
            for(int i=0;i<count;i++){int k=(start+i)%Capacity;meanTime+=times[k]-time;meanPosition+=positions[k]-basePosition;}
            meanTime/=count;meanPosition/=count;
            for(int i=0;i<count;i++){int k=(start+i)%Capacity;double offset=times[k]-time-meanTime;denominator+=offset*offset;numerator+=(positions[k]-basePosition-meanPosition)*(float)offset;}
            if(denominator<1e-10)return;
            var measured=numerator/(float)denominator;float blend=response<=0?1:1-Mathf.Exp(-(float)dt/response);
            Velocity=Vector3.Lerp(Velocity,measured,blend);
        }
    }
}
