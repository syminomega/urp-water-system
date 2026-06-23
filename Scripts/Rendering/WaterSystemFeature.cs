using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_6000_0_OR_NEWER
using UnityEngine.Rendering.RenderGraphModule;
#endif
using UnityEngine.Rendering.Universal;

namespace WaterSystem
{
    public class WaterSystemFeature : ScriptableRendererFeature
    {

        #region Water Effects Pass

        class WaterFxPass : ScriptableRenderPass
        {
            private const string k_RenderWaterFXTag = "Render Water FX";
            private const string k_WaterFXMapName = "_WaterFXMap";
            private static readonly int k_WaterFXMapId = Shader.PropertyToID(k_WaterFXMapName);
            private ProfilingSampler m_WaterFX_Profile = new ProfilingSampler(k_RenderWaterFXTag);
            private readonly ShaderTagId m_WaterFXShaderTag = new ShaderTagId("WaterFX");
            private readonly Color m_ClearColor = new Color(0.0f, 0.5f, 0.5f, 0.5f); //r = foam mask, g = normal.x, b = normal.z, a = displacement
            private FilteringSettings m_FilteringSettings;
#if UNITY_2022_1_OR_NEWER
            private RTHandle m_WaterFX;
            private RenderTextureDescriptor m_WaterFXDescriptor;
#else
            private RenderTargetHandle m_WaterFX = RenderTargetHandle.CameraTarget;
#endif

#if UNITY_6000_0_OR_NEWER
            class PassData
            {
                public RendererListHandle rendererListHandle;
            }
#endif

            public WaterFxPass()
            {
#if !UNITY_2022_1_OR_NEWER
                m_WaterFX.Init(k_WaterFXMapName);
#endif
                // only wanting to render transparent objects
                m_FilteringSettings = new FilteringSettings(RenderQueueRange.transparent);
            }

            // Calling Configure since we are wanting to render into a RenderTexture and control cleat
            public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
            {
                // no need for a depth buffer
                cameraTextureDescriptor.depthBufferBits = 0;
#if UNITY_2022_1_OR_NEWER
                cameraTextureDescriptor.depthStencilFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.None;
#endif
                // Half resolution
                cameraTextureDescriptor.width /= 2;
                cameraTextureDescriptor.height /= 2;
                // default format TODO research usefulness of HDR format
                cameraTextureDescriptor.colorFormat = RenderTextureFormat.Default;
                // get a temp RT for rendering into
#if UNITY_2022_1_OR_NEWER
                if (m_WaterFX == null || !m_WaterFXDescriptor.Equals(cameraTextureDescriptor))
                {
                    m_WaterFX?.Release();
                    m_WaterFX = RTHandles.Alloc(cameraTextureDescriptor, FilterMode.Bilinear, name: k_WaterFXMapName);
                    m_WaterFXDescriptor = cameraTextureDescriptor;
                }
                ConfigureTarget(m_WaterFX);
                cmd.SetGlobalTexture(k_WaterFXMapId, m_WaterFX.nameID);
#else
                cmd.GetTemporaryRT(m_WaterFX.id, cameraTextureDescriptor, FilterMode.Bilinear);
                ConfigureTarget(m_WaterFX.Identifier());
#endif
                // clear the screen with a specific color for the packed data
                ConfigureClear(ClearFlag.Color, m_ClearColor);
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                CommandBuffer cmd = CommandBufferPool.Get();
                using (new ProfilingScope(cmd, m_WaterFX_Profile)) // makes sure we have profiling ability
                {
                    context.ExecuteCommandBuffer(cmd);
                    cmd.Clear();

                    // here we choose renderers based off the "WaterFX" shader pass and also sort back to front
                    var drawSettings = CreateDrawingSettings(m_WaterFXShaderTag, ref renderingData,
                        SortingCriteria.CommonTransparent);

                    // draw all the renderers matching the rules we setup
                    context.DrawRenderers(renderingData.cullResults, ref drawSettings, ref m_FilteringSettings);
                }
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }

#if UNITY_6000_0_OR_NEWER
            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
                UniversalLightData lightData = frameData.Get<UniversalLightData>();

                RenderTextureDescriptor descriptor = cameraData.cameraTargetDescriptor;
                descriptor.depthBufferBits = 0;
                descriptor.depthStencilFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.None;
                descriptor.width /= 2;
                descriptor.height /= 2;
                descriptor.colorFormat = RenderTextureFormat.Default;

                var waterFXMap = UniversalRenderer.CreateRenderGraphTexture(renderGraph, descriptor, k_WaterFXMapName, true,
                    FilterMode.Bilinear);

                using (var builder = renderGraph.AddRasterRenderPass<PassData>(k_RenderWaterFXTag, out var passData,
                           m_WaterFX_Profile))
                {
                    var drawSettings = RenderingUtils.CreateDrawingSettings(m_WaterFXShaderTag, renderingData, cameraData,
                        lightData, SortingCriteria.CommonTransparent);
                    var rendererListParams = new RendererListParams(renderingData.cullResults, drawSettings,
                        m_FilteringSettings);

                    passData.rendererListHandle = renderGraph.CreateRendererList(rendererListParams);

                    if (!passData.rendererListHandle.IsValid())
                        return;

                    builder.UseRendererList(passData.rendererListHandle);
                    builder.SetRenderAttachment(waterFXMap, 0, AccessFlags.Write);
                    builder.SetGlobalTextureAfterPass(waterFXMap, k_WaterFXMapId);

                    builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                    {
                        context.cmd.ClearRenderTarget(false, true, m_ClearColor);
                        context.cmd.DrawRendererList(data.rendererListHandle);
                    });
                }
            }
#endif

            public override void OnCameraCleanup(CommandBuffer cmd) 
            {
#if !UNITY_2022_1_OR_NEWER
                // since the texture is used within the single cameras use we need to cleanup the RT afterwards
                cmd.ReleaseTemporaryRT(m_WaterFX.id);
#endif
            }

            public void Dispose()
            {
#if UNITY_2022_1_OR_NEWER
                m_WaterFX?.Release();
                m_WaterFX = null;
#endif
            }
        }

        #endregion

        #region Caustics Pass

        class WaterCausticsPass : ScriptableRenderPass
        {
            private const string k_RenderWaterCausticsTag = "Render Water Caustics";
            private ProfilingSampler m_WaterCaustics_Profile = new ProfilingSampler(k_RenderWaterCausticsTag);
            public Material WaterCausticMaterial;
            private static Mesh m_mesh;

#if UNITY_6000_0_OR_NEWER
            class PassData
            {
                public Material material;
                public Matrix4x4 mainLightMatrix;
                public Matrix4x4 drawMatrix;
            }
#endif

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                var cam = renderingData.cameraData.camera;
                // Stop the pass rendering in the preview or material missing
                if (cam.cameraType == CameraType.Preview || !WaterCausticMaterial)
                    return;

                CommandBuffer cmd = CommandBufferPool.Get();
                using (new ProfilingScope(cmd, m_WaterCaustics_Profile))
                {
                    var sunMatrix = RenderSettings.sun != null
                        ? RenderSettings.sun.transform.localToWorldMatrix
                        : Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(-45f, 45f, 0f), Vector3.one);
                    WaterCausticMaterial.SetMatrix("_MainLightDir", sunMatrix);
                
                
                    // Create mesh if needed
                    if (!m_mesh)
                        m_mesh = GenerateCausticsMesh(1000f);

                    // Create the matrix to position the caustics mesh.
                    var position = cam.transform.position;
                    position.y = 0; // TODO should read a global 'water height' variable.
                    var matrix = Matrix4x4.TRS(position, Quaternion.identity, Vector3.one);
                    // Setup the CommandBuffer and draw the mesh with the caustic material and matrix
                    cmd.DrawMesh(m_mesh, matrix, WaterCausticMaterial, 0, 0);
                }

                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }

#if UNITY_6000_0_OR_NEWER
            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                var cam = cameraData.camera;
                if (cam.cameraType == CameraType.Preview || !WaterCausticMaterial)
                    return;

                if (!m_mesh)
                    m_mesh = GenerateCausticsMesh(1000f);

                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                if (!resourceData.activeColorTexture.IsValid() || !resourceData.activeDepthTexture.IsValid())
                    return;

                using (var builder = renderGraph.AddRasterRenderPass<PassData>(k_RenderWaterCausticsTag, out var passData,
                           m_WaterCaustics_Profile))
                {
                    passData.material = WaterCausticMaterial;
                    passData.mainLightMatrix = RenderSettings.sun != null
                        ? RenderSettings.sun.transform.localToWorldMatrix
                        : Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(-45f, 45f, 0f), Vector3.one);

                    var position = cam.transform.position;
                    position.y = 0;
                    passData.drawMatrix = Matrix4x4.TRS(position, Quaternion.identity, Vector3.one);

                    builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.Write);
                    builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.Write);
                    builder.AllowGlobalStateModification(true);

                    builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                    {
                        data.material.SetMatrix("_MainLightDir", data.mainLightMatrix);
                        context.cmd.DrawMesh(m_mesh, data.drawMatrix, data.material, 0, 0);
                    });
                }
            }
#endif
        }

        #endregion

        WaterFxPass m_WaterFxPass;
        WaterCausticsPass m_CausticsPass;

        public WaterSystemSettings settings = new WaterSystemSettings();
        [HideInInspector][SerializeField] private Shader causticShader;
        [HideInInspector][SerializeField] private Texture2D causticTexture;

        private Material _causticMaterial;

        private static readonly int SrcBlend = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlend = Shader.PropertyToID("_DstBlend");
        private static readonly int Size = Shader.PropertyToID("_Size");
        private static readonly int CausticTexture = Shader.PropertyToID("_CausticMap");

        public override void Create()
        {
            // WaterFX Pass
            m_WaterFxPass = new WaterFxPass {renderPassEvent = RenderPassEvent.BeforeRenderingOpaques};

            // Caustic Pass
            m_CausticsPass = new WaterCausticsPass();

            causticShader = causticShader ? causticShader : Shader.Find("Hidden/BoatAttack/Caustics");
            if (causticShader == null) return;
            if (_causticMaterial)
            {
                DestroyImmediate(_causticMaterial);
            }
            _causticMaterial = CoreUtils.CreateEngineMaterial(causticShader);
            _causticMaterial.SetFloat("_BlendDistance", settings.causticBlendDistance);
            
            if (causticTexture == null)
            {
                Debug.Log("Caustics Texture missing, attempting to load.");
#if UNITY_EDITOR
                causticTexture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/com.verasl.water-system/Textures/WaterSurface_single.tif");
#endif
            }
            _causticMaterial.SetTexture(CausticTexture, causticTexture);
            
            switch (settings.debug)
            {
                case WaterSystemSettings.DebugMode.Caustics:
                    _causticMaterial.SetFloat(SrcBlend, 1f);
                    _causticMaterial.SetFloat(DstBlend, 0f);
                    _causticMaterial.EnableKeyword("_DEBUG");
                    m_CausticsPass.renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
                    break;
                case WaterSystemSettings.DebugMode.WaterEffects:
                    break;
                case WaterSystemSettings.DebugMode.Disabled:
                    // Caustics
                    _causticMaterial.SetFloat(SrcBlend, 2f);
                    _causticMaterial.SetFloat(DstBlend, 0f);
                    _causticMaterial.DisableKeyword("_DEBUG");
                    m_CausticsPass.renderPassEvent = RenderPassEvent.AfterRenderingSkybox + 1;
                    // WaterEffects
                    break;
            }

            _causticMaterial.SetFloat(Size, settings.causticScale);
            m_CausticsPass.WaterCausticMaterial = _causticMaterial;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            renderer.EnqueuePass(m_WaterFxPass);
            renderer.EnqueuePass(m_CausticsPass);
        }

        protected override void Dispose(bool disposing)
        {
            m_WaterFxPass?.Dispose();
        }

        /// <summary>
        /// This function Generates a flat quad for use with the caustics pass.
        /// </summary>
        /// <param name="size">The length of the quad.</param>
        /// <returns></returns>
        private static Mesh GenerateCausticsMesh(float size)
        {
            var m = new Mesh();
            size *= 0.5f;

            var verts = new[]
            {
                new Vector3(-size, 0f, -size),
                new Vector3(size, 0f, -size),
                new Vector3(-size, 0f, size),
                new Vector3(size, 0f, size)
            };
            m.vertices = verts;

            var tris = new[]
            {
                0, 2, 1,
                2, 3, 1
            };
            m.triangles = tris;

            return m;
        }

        [System.Serializable]
        public class WaterSystemSettings
        {
            [Header("Caustics Settings")] [Range(0.1f, 1f)]
            public float causticScale = 0.25f;

            public float causticBlendDistance = 3f;

            [Header("Advanced Settings")] public DebugMode debug = DebugMode.Disabled;

            public enum DebugMode
            {
                Disabled,
                WaterEffects,
                Caustics
            }
        }
    }
}
