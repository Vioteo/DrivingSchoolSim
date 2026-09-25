using UnityEngine;
using UnityEngine.InputSystem;

namespace DrivingSchool.Presentation
{
    /// <summary>Low-speed layout test vehicle, not the production tyre/clutch/G27 solver.</summary>
    public sealed class TrainingVehicle : MonoBehaviour
    {
        public float wheelbase=2.72f, width=2.25f, length=4.5f;
        public bool driving;
        public float Speed { get; private set; }
        public int Gear { get; private set; } = 1;
        public int ContactCount { get; private set; }
        float yaw, steering, contactCooldown;
        Rigidbody body;
        Transform leftWheel, rightWheel;
        Quaternion leftRest, rightRest;
        public bool handbrake;
        // An explicit device also makes automated keyboard input independent of other attached keyboards.
        public Keyboard KeyboardDevice { get; set; }
        void Update()
        {
            var k=KeyboardDevice ?? Keyboard.current;
            if(!driving || k==null)return;
            if(k.qKey.wasPressedThisFrame && Mathf.Abs(Speed)<.1f)Gear=Gear==-1?1:-1;
            if(k.eKey.wasPressedThisFrame && Gear>0)Gear=Gear==1?2:1;
        }
        void Awake()
        {
            body=GetComponent<Rigidbody>(); yaw=transform.eulerAngles.y;
            foreach(var t in GetComponentsInChildren<Transform>())
            { if(t.name=="Wheel_FL")leftWheel=t; if(t.name=="Wheel_FR")rightWheel=t; }
            if(leftWheel)leftRest=leftWheel.localRotation;
            if(rightWheel)rightRest=rightWheel.localRotation;
        }
        public void ResetAt(Vector3 position, float heading)
        {
            Speed=0; Gear=1; yaw=heading; steering=0; ContactCount=0; contactCooldown=0;
            transform.SetPositionAndRotation(position,Quaternion.Euler(0,heading,0));
            if(body) { body.position=position; body.rotation=transform.rotation; }
            UnityEngine.Physics.SyncTransforms();
        }
        void FixedUpdate()
        {
            var k=KeyboardDevice ?? Keyboard.current;
            float dt=Time.fixedDeltaTime;
            if(!driving || (!Application.isFocused && !Application.isBatchMode) || k==null) { Speed=0; return; }
            float input=(k.dKey.isPressed||k.rightArrowKey.isPressed?1:0)-(k.aKey.isPressed||k.leftArrowKey.isPressed?1:0);
            steering=Mathf.MoveTowards(steering,input,dt*2.2f);
            bool throttle=k.wKey.isPressed||k.upArrowKey.isPressed;
            handbrake=k.spaceKey.isPressed;
            bool brake=k.sKey.isPressed||k.downArrowKey.isPressed||handbrake;
            float accel=throttle?(Gear<0?-1.8f:2.2f):0;
            float slope=Vector3.Dot(transform.forward,Vector3.up);
            Speed+= (accel-slope*9.81f)*dt;
            Speed=Mathf.MoveTowards(Speed,0,(brake?8:throttle?.1f:.25f)*dt);
            Speed=Mathf.Clamp(Speed,-2.2f,Gear==2?9:4.1f);
            if(handbrake && Mathf.Abs(Speed)<.2f)Speed=0;
            float steerDegrees=steering*32;
            float nextYaw=yaw+Speed/wheelbase*Mathf.Tan(steerDegrees*Mathf.Deg2Rad)*Mathf.Rad2Deg*dt;
            Quaternion heading=Quaternion.Euler(0,nextYaw,0);
            Vector3 motion=heading*Vector3.forward*(Speed*dt);
            Vector3 candidate=transform.position+motion;
            // Layer 10 contains solid props only; road/ramp support is layer 9.
            bool blocked=UnityEngine.Physics.CheckBox(candidate+Vector3.up*.75f,new Vector3(width/2,.6f,length/2),heading,1<<10,QueryTriggerInteraction.Ignore);
            contactCooldown-=dt;
            if(blocked)
            {
                if(contactCooldown<=0 && Mathf.Abs(Speed)>.1f) { ContactCount++; contactCooldown=1.5f; }
                Speed=0; return;
            }
            yaw=nextYaw;
            float front=Surface(candidate+heading*new Vector3(0,0,wheelbase/2));
            float rear=Surface(candidate+heading*new Vector3(0,0,-wheelbase/2));
            candidate.y=(front+rear)*.5f;
            Quaternion rotation=heading*Quaternion.Euler(-Mathf.Atan2(front-rear,wheelbase)*Mathf.Rad2Deg,0,0);
            body.MovePosition(candidate); body.MoveRotation(rotation);
            if(leftWheel)leftWheel.localRotation=leftRest*Quaternion.AngleAxis(steerDegrees,Vector3.forward);
            if(rightWheel)rightWheel.localRotation=rightRest*Quaternion.AngleAxis(steerDegrees,Vector3.forward);
        }
        static float Surface(Vector3 p)
        {
            return UnityEngine.Physics.Raycast(new Vector3(p.x,5,p.z),Vector3.down,out var hit,8,1<<9,QueryTriggerInteraction.Ignore)?hit.point.y:0;
        }
    }
}
