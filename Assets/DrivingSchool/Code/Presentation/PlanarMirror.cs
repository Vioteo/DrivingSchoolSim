using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace DrivingSchool.Presentation
{
    // Mono-view prototype. XR per-eye rendering remains an explicit acceptance gate.
    public sealed class PlanarMirror : MonoBehaviour
    {
        public Transform surface; public Camera source;
        public Material templateMaterial;
        Camera reflection; RenderTexture texture; Material material;
        static bool rendering;
        void OnEnable(){RenderPipelineManager.beginCameraRendering+=Render;}
        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering-=Render;
            if(texture!=null){texture.Release();Destroy(texture);texture=null;}
            if(reflection!=null){Destroy(reflection.gameObject);reflection=null;}
            if(material!=null){Destroy(material);material=null;}
        }
        void Render(ScriptableRenderContext context,Camera camera)
        {
            if(rendering||camera!=source||surface==null)return;
            if(reflection==null)
            {
                reflection=new GameObject("Mirror reflection",typeof(Camera)).GetComponent<Camera>();reflection.enabled=false;
                texture=new RenderTexture(512,256,16);texture.Create();
                if(templateMaterial!=null)
                {
                    material=new Material(templateMaterial);
                }
                else
                {
                    var s=Shader.Find("Universal Render Pipeline/Lit")??Shader.Find("Universal Render Pipeline/Unlit")??Shader.Find("Unlit/Texture");
                    material=s!=null?new Material(s):null;
                }
                if(material!=null)
                {
                    material.mainTexture=texture;
                    if(material.HasProperty("_BaseMap"))material.SetTexture("_BaseMap",texture);
                    var rend=surface.GetComponent<Renderer>();
                    if(rend!=null)rend.sharedMaterial=material;
                }
                reflection.GetUniversalAdditionalCameraData().renderShadows=false;
            }
            // The authored surface normal points back into the cabin.
            var normal=-surface.forward;var p=surface.position;float d=-Vector3.Dot(normal,p);
            var plane=new Vector4(normal.x,normal.y,normal.z,d);
            var m=Matrix4x4.identity;
            for(int r=0;r<3;r++)for(int c=0;c<4;c++)m[r,c]-=2*plane[r]*plane[c];
            reflection.CopyFrom(source);reflection.enabled=false;reflection.targetTexture=texture;
            reflection.cullingMask=source.cullingMask&~(1<<surface.gameObject.layer);
            reflection.transform.position=m.MultiplyPoint(source.transform.position);
            reflection.worldToCameraMatrix=source.worldToCameraMatrix*m;
            var pos=reflection.worldToCameraMatrix.MultiplyPoint(p+normal*.015f);var n=reflection.worldToCameraMatrix.MultiplyVector(normal).normalized;
            reflection.projectionMatrix=source.CalculateObliqueMatrix(new Vector4(n.x,n.y,n.z,-Vector3.Dot(pos,n)));
            bool before=GL.invertCulling;
#pragma warning disable CS0618
            try{rendering=true;GL.invertCulling=!before;UniversalRenderPipeline.RenderSingleCamera(context,reflection);}
            finally{GL.invertCulling=before;rendering=false;}
#pragma warning restore CS0618
        }
    }
}
