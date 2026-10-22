using UnityEngine;
using UnityEngine.InputSystem;
using Starframe.App;
namespace Starframe.Flight
{
    public sealed class ShipFlightController : MonoBehaviour
    {
        Rigidbody body;FlightShipRuntime runtime;FlightTuning tuning;Camera viewCamera;RangeHUD hud;
        Vector3 velocity,angularVelocity,moveInput,mainThrustDemand;
        Vector2 aimInput,smoothedSteering;
        float rollInput,hullRadius,maneuverDemand;
        bool boostHeld;
        public float BoostPowerRatio{get;private set;}
        public float Energy=>runtime?runtime.Power.Energy:0;
        public float MaxEnergy=>runtime?runtime.Power.Capacity:0;
        public bool RequestedBoost=>boostHeld&&runtime&&runtime.IsAlive&&(!hud||!hud.ExitDialogOpen)&&(ExternalControl||(Application.isFocused&&!GameInput.Suspended));
        public bool IsBoosting=>RequestedBoost&&BoostPowerRatio>0;
        public float BoostScale=>1+(RequestedBoost?Mathf.Max(0,tuning.boostMultiplier-1)*BoostPowerRatio:0);
        // Kept for callers using the old allocation field; base flight is independent of this ratio.
        public float PowerRatio{get=>BoostPowerRatio;set=>SetBoostPowerRatio(value);}
        public void SetBoostPowerRatio(float value)=>BoostPowerRatio=Mathf.Clamp01(value);
        public Vector2 AimInput=>aimInput;
        public float AimRadiusPixels=>Mathf.Max(1,Mathf.Min(Screen.height*.5f*Mathf.Tan(tuning.maxAimOffsetDegrees*Mathf.Deg2Rad)/Mathf.Tan(viewCamera.fieldOfView*.5f*Mathf.Deg2Rad),Mathf.Min(Screen.width,Screen.height)*.4f));
        public Vector2 AimOffsetPixels=>aimInput*AimRadiusPixels;
        public Vector3 Velocity=>velocity;
        public Vector3 Forward=>body?body.rotation*Vector3.forward:transform.forward;
        public float HullRadius=>hullRadius;
        public FlightTuning Tuning=>tuning;
        public float CurrentSpeed=>velocity.magnitude;
        public ShipPerformance Performance=>runtime.Performance;
        public bool ExternalControl;
        public float RequestedThrustFraction=>Mathf.Clamp01(moveInput.magnitude+SteeringInput().magnitude+Mathf.Abs(rollInput)+(velocity.sqrMagnitude>.001f?.25f:0));
        public void SetExternalInput(Vector3 move,Vector2 aim,float roll=0,bool boost=false)
        {
            ExternalControl=true;float sum=Mathf.Abs(move.x)+Mathf.Abs(move.y)+Mathf.Abs(move.z);
            moveInput=sum>1?move/sum:move;aimInput=Vector2.ClampMagnitude(aim,1);rollInput=Mathf.Clamp(roll,-1,1);boostHeld=boost;
        }
        public void ResetMotion(Vector3 position,Quaternion rotation)
        {
            velocity=angularVelocity=moveInput=mainThrustDemand=Vector3.zero;aimInput=smoothedSteering=Vector2.zero;rollInput=maneuverDemand=0;boostHeld=false;BoostPowerRatio=0;
            body.position=position;body.rotation=rotation;
        }
        public void Initialize(Rigidbody rigidbody,FlightShipRuntime ship,FlightTuning values,Camera camera,float radius)
        {
            body=rigidbody;runtime=ship;tuning=values;viewCamera=camera;hullRadius=radius;aimInput=smoothedSteering=Vector2.zero;
        }
        public void BindHUD(RangeHUD value)=>hud=value;
        void Update()
        {
            if(!runtime)return;
            if(ExternalControl)return;
            bool dialog=(hud&&hud.ExitDialogOpen)||GameInput.Suspended;
            var lockMode=dialog?CursorLockMode.None:CursorLockMode.Locked;
            bool acquired=Cursor.lockState!=lockMode;
            if(acquired)Cursor.lockState=lockMode;
            if(Cursor.visible!=dialog)Cursor.visible=dialog;
            moveInput=Vector3.zero;rollInput=0;boostHeld=false;
            if(!Application.isFocused)return;
            if(!dialog){
                moveInput=new Vector3((GameInput.Held(GameAction.Right)?1:0)-(GameInput.Held(GameAction.Left)?1:0),(GameInput.Held(GameAction.Up)?1:0)-(GameInput.Held(GameAction.Down)?1:0),(GameInput.Held(GameAction.Forward)?1:0)-(GameInput.Held(GameAction.Backward)?1:0));
                float sum=Mathf.Abs(moveInput.x)+Mathf.Abs(moveInput.y)+Mathf.Abs(moveInput.z);if(sum>1)moveInput/=sum;
                rollInput=(GameInput.Held(GameAction.RollLeft)?1:0)-(GameInput.Held(GameAction.RollRight)?1:0);
                boostHeld=GameInput.Held(GameAction.Boost);
            }
            var mouse=Mouse.current;
            if(!dialog&&!acquired&&mouse!=null&&viewCamera){
                Vector2 delta=mouse.delta.ReadValue()*GameSettings.Current.mouseSensitivity;
                if(GameSettings.Current.invertY)delta.y=-delta.y;
                if(delta.sqrMagnitude>.0001f){
                    // A persistent virtual stick: ship rotation never consumes or recentres the offset.
                    float scale=tuning.mouseDegreesPerPixel*tuning.aimSensitivity*1080f/Mathf.Max(1,Screen.height)/Mathf.Max(1,tuning.maxAimOffsetDegrees);
                    aimInput=Vector2.ClampMagnitude(aimInput+delta*scale,1);
                }
            }
        }
        Vector2 SteeringInput()
        {
            float radius=aimInput.magnitude,deadZone=Mathf.Clamp(tuning.aimInputDeadZone,0,.4f);
            if(radius<=deadZone)return Vector2.zero;
            float strength=Mathf.Pow(Mathf.Clamp01((radius-deadZone)/(1-deadZone)),Mathf.Max(.1f,tuning.aimResponseExponent));
            return aimInput/radius*strength;
        }
        public float ThrusterThrottle(Vector3 localThrustDirection)
        {
            if(!runtime||!Performance.coreAlive)return 0;
            float main=Mathf.Clamp01(Vector3.Dot(localThrustDirection.normalized,mainThrustDemand));
            return Mathf.Clamp01(((1-tuning.maneuverThrustFraction)*main+tuning.maneuverThrustFraction*maneuverDemand)*BoostScale);
        }
        void FixedUpdate()
        {
            if(!body||!runtime)return;
            var p=Performance;float dt=Time.fixedDeltaTime,boost=BoostScale;mainThrustDemand=Vector3.zero;maneuverDemand=0;
            if(!p.coreAlive){velocity=Vector3.zero;angularVelocity=Vector3.zero;smoothedSteering=Vector2.zero;return;}
            bool dialog=(hud&&hud.ExitDialogOpen)||(!ExternalControl&&GameInput.Suspended);
            var rotation=body.rotation;var inverse=Quaternion.Inverse(rotation);
            bool steeringDisabled=dialog||(!ExternalControl&&!Application.isFocused);
            var requestedStick=steeringDisabled?Vector2.zero:SteeringInput();
            float smoothing=tuning.aimInputSmoothing*(ExternalControl?1:GameSettings.Current.aimSmoothing);
            float inputBlend=smoothing<=0?1:1-Mathf.Exp(-dt/smoothing);
            smoothedSteering=steeringDisabled?Vector2.zero:Vector2.Lerp(smoothedSteering,requestedStick,inputBlend);
            if(requestedStick==Vector2.zero&&smoothedSteering.sqrMagnitude<.000001f)smoothedSteering=Vector2.zero;
            var stick=smoothedSteering;
            var turnInput=new Vector3(-stick.y,stick.x,0);
            float turnDemand=Mathf.Abs(turnInput.x)+Mathf.Abs(turnInput.y)+Mathf.Abs(rollInput);
            var local=inverse*velocity;
            float brakeDemand=0;for(int i=0;i<3;i++)if(Mathf.Abs(moveInput[i])<.001f&&Mathf.Abs(local[i])>.02f)brakeDemand+=1;
            float controlShare=1/Mathf.Max(1,turnDemand+brakeDemand);
            // Keep steering responsive while the translation axes automatically brake.
            float turnShare=Mathf.Max(controlShare,Mathf.Clamp01(tuning.minimumTurnAuthority));
            for(int i=0;i<3;i++){
                if(Mathf.Abs(moveInput[i])>.001f){
                    float acceleration=moveInput[i]>0?p.accelerationPositive[i]:p.accelerationNegative[i];
                    local[i]+=moveInput[i]*acceleration*boost*dt;mainThrustDemand[i]=moveInput[i];
                }else{
                    float opposite=local[i]>0?p.accelerationNegative[i]:p.accelerationPositive[i];
                    float deceleration=opposite+p.assistAcceleration*controlShare;
                    float brake=Mathf.Clamp01(Mathf.Abs(local[i])/Mathf.Max(.1f,deceleration*.15f));
                    mainThrustDemand[i]=-Mathf.Sign(local[i])*brake;
                    maneuverDemand=Mathf.Max(maneuverDemand,brake*controlShare);
                    local[i]=Mathf.MoveTowards(local[i],0,deceleration*boost*dt);
                }
            }
            velocity=Vector3.ClampMagnitude(rotation*local,p.maxSpeed);
            Vector3 step=Vector3.zero;
            for(int i=0;i<2;i++){
                float acceleration=(i==0?p.pitchAcceleration:p.yawAcceleration)*boost*turnShare;
                // Offset requests angular velocity, rather than arrival at a world heading.
                float target=turnInput[i]*p.angularRate*boost;
                float previous=angularVelocity[i];
                float filteredRate=Mathf.Lerp(previous,target,1-Mathf.Exp(-dt/Mathf.Max(.05f,tuning.turnResponseTime)));
                angularVelocity[i]=Mathf.MoveTowards(previous,filteredRate,acceleration*dt);
                if(Mathf.Abs(target)<.001f&&Mathf.Abs(angularVelocity[i])<.02f)angularVelocity[i]=0;
                step[i]=angularVelocity[i]*dt;
                float rateDemand=Mathf.Abs(target)/Mathf.Max(1,p.angularRate);
                float accelerationDemand=Mathf.Abs(angularVelocity[i]-previous)/Mathf.Max(.001f,acceleration*dt);
                maneuverDemand=Mathf.Max(maneuverDemand,Mathf.Max(rateDemand,accelerationDemand)*turnShare);
            }
            float desiredRoll=rollInput*p.rollSpeed*boost;
            angularVelocity.z=Mathf.MoveTowards(angularVelocity.z,desiredRoll,p.rollSpeed*boost*dt/Mathf.Max(.05f,tuning.rollBlendTime));
            step.z=angularVelocity.z*dt;maneuverDemand=Mathf.Max(maneuverDemand,Mathf.Abs(angularVelocity.z)/Mathf.Max(1,p.rollSpeed));
            var next=body.position+velocity*dt;
            float limit=Mathf.Max(1,tuning.arenaHalfExtent-hullRadius-1);
            for(int i=0;i<3;i++){
                float distance=limit-Mathf.Abs(body.position[i]);
                bool outward=body.position[i]*velocity[i]>0;
                if(outward){
                    float allowed=Mathf.Sqrt(2*tuning.boundaryBrakeAcceleration*Mathf.Max(0,distance));
                    velocity[i]=Mathf.Sign(velocity[i])*Mathf.Min(Mathf.Abs(velocity[i]),allowed);
                }
                next[i]=body.position[i]+velocity[i]*dt;
                if(Mathf.Abs(next[i])>=limit){next[i]=Mathf.Clamp(next[i],-limit,limit);velocity[i]=0;}
            }
            var displacement=next-body.position;
            if(displacement.sqrMagnitude>.000001f&&body.SweepTest(displacement.normalized,out var obstacle,displacement.magnitude,QueryTriggerInteraction.Ignore)){
                bool blocked=true;
                if(SeparatingWreckage(obstacle.collider)){
                    blocked=false;float closest=float.PositiveInfinity;
                    foreach(var hit in body.SweepTestAll(displacement.normalized,displacement.magnitude,QueryTriggerInteraction.Ignore))if(!SeparatingWreckage(hit.collider)&&hit.distance<closest){obstacle=hit;closest=hit.distance;blocked=true;}
                }
                if(blocked){next=body.position+displacement.normalized*Mathf.Max(0,obstacle.distance-.01f);
                    if(Vector3.Dot(velocity,obstacle.normal)<0)velocity=Vector3.ProjectOnPlane(velocity,obstacle.normal);}
            }
            body.MovePosition(next);
            float degrees=step.magnitude;
            if(degrees>.0001f)body.MoveRotation(rotation*Quaternion.AngleAxis(degrees,step/degrees));
        }
        void OnDisable(){Cursor.lockState=CursorLockMode.None;Cursor.visible=true;}
        bool SeparatingWreckage(Collider collider){var part=collider?collider.GetComponent<Starframe.Combat.DebrisPartHitbox>():null;return part&&part.Cluster&&part.Cluster.SuppressSourceCollisions&&part.Cluster.SourceShip==runtime;}
    }
    [DefaultExecutionOrder(50)]
    public sealed class FlightChaseCamera : MonoBehaviour
    {
        Transform ship;ShipFlightController flight;FlightTuning tuning;RangeHUD hud;
        float distance,targetDistance,minDistance,maxDistance;
        public void Initialize(Transform target,ShipFlightController controller,FlightTuning values)
        {
            ship=target;flight=controller;tuning=values;float radius=controller.HullRadius;
            minDistance=Mathf.Max(values.minCameraDistance,radius*1.9f);
            maxDistance=Mathf.Max(values.maxCameraDistance,radius*6);
            distance=targetDistance=Mathf.Clamp(Mathf.Max(values.cameraDistance,radius*3),minDistance,maxDistance);
            transform.rotation=ship.rotation;Place();
        }
        public void BindHUD(RangeHUD value)=>hud=value;
        public void ResetFollow(){if(!ship)return;transform.rotation=ship.rotation;Place();}
        void Place()
        {
            float fieldOfView=GameSettings.Current.fieldOfView;
            var camera=GetComponent<Camera>();if(camera)camera.fieldOfView=fieldOfView;
            float height=distance*2*(.5f-tuning.cameraShipViewportY)*Mathf.Tan(fieldOfView*.5f*Mathf.Deg2Rad);
            // Orbit and orientation use the same filtered basis; translation is anchored to the ship.
            transform.position=ship.position+transform.rotation*new Vector3(0,height,-distance);
        }
        void LateUpdate()
        {
            if(!ship)return;
            if(!GameInput.Suspended&&(!hud||!hud.ExitDialogOpen)&&Mouse.current!=null){
                float scroll=Mouse.current.scroll.ReadValue().y;if(Mathf.Abs(scroll)>10)scroll/=120f;
                targetDistance=Mathf.Clamp(targetDistance*Mathf.Exp(-scroll*tuning.cameraZoomSensitivity),minDistance,maxDistance);
            }
            float dt=Mathf.Min(Time.deltaTime,.1f);
            distance=Mathf.Lerp(distance,targetDistance,1-Mathf.Exp(-tuning.cameraPositionSharpness*dt));
            transform.rotation=Quaternion.Slerp(transform.rotation,ship.rotation,1-Mathf.Exp(-tuning.cameraRotationSharpness*dt));
            Place();
        }
    }
}
