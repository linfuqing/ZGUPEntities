using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ZG
{
    public class FilterEffectRenderPassFeature : ScriptableRendererFeature
    {
        private class TransparentRenderPass : ScriptableRenderPass
        {
            public static readonly int FilterWeight = Shader.PropertyToID("g_FilterWeight");

            private ProfilingSampler __profilingSampler = new ProfilingSampler("FilterEffectTransparent");
            private RenderFilterEffect __renderFilterEffect;
            private ShaderTagId __shaderTagId = new ShaderTagId("Filter");
            private FilteringSettings __filteringSettings;

            public TransparentRenderPass(int layerMask)
            {
                __filteringSettings = new FilteringSettings(RenderQueueRange.transparent, layerMask);

                renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            }

            public void Init(RenderFilterEffect renderFilterEffect)
            {
                __renderFilterEffect = renderFilterEffect;
            }

            // This method is called before executing the render pass.
            // It can be used to configure render targets and their clear state. Also to create temporary render target textures.
            // When empty this render pass will render to the active camera render target.
            // You should never call CommandBuffer.SetRenderTarget. Instead call <c>ConfigureTarget</c> and <c>ConfigureClear</c>.
            // The render pipeline will ensure target setup and clearing happens in a performant manner.
            /*public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
            }*/

            // Here you can implement the rendering logic.
            // Use <c>ScriptableRenderContext</c> to issue drawing commands or execute command buffers
            // https://docs.unity3d.com/ScriptReference/Rendering.ScriptableRenderContext.html
            // You don't have to call ScriptableRenderContext.submit, the render pipeline will call it at specific points in the pipeline.
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                CommandBuffer cmd = CommandBufferPool.Get();
                using (new ProfilingScope(cmd, __profilingSampler))
                {
                    var drawingSettings = CreateDrawingSettings(__shaderTagId, ref renderingData, SortingCriteria.CommonTransparent);
                    /*if (RenderFilterEffect.activeCount > 0)
                    {
                        __renderFilterEffect.UpdateDestinations();

                        //drawingSettings.overrideMaterial = __material;
                        //drawingSettings.overrideMaterialPassIndex = 0;
                        cmd.SetGlobalFloat(FilterWeight, 1.0f);
                    }
                    else*/
                    cmd.SetGlobalFloat(FilterWeight, 0.0f);

                    context.ExecuteCommandBuffer(cmd);

                    context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref __filteringSettings);
                }

                CommandBufferPool.Release(cmd);
            }

            // Cleanup any allocated resources that were created during the execution of this render pass.
            /*public override void OnCameraCleanup(CommandBuffer cmd)
            {
            }*/
        }

        private class OpaqueRenderPass : ScriptableRenderPass
        {
            public static readonly int FilterParamsLength = Shader.PropertyToID("g_FilterParamsLength");
            public static readonly int FilterParams = Shader.PropertyToID("g_FilterParams");
            public static readonly int FilterSourceColor = Shader.PropertyToID("g_FilterSourceColor");
            public static readonly int FilterDestinationColor = Shader.PropertyToID("g_FilterDestinationColor");

            private ProfilingSampler __profilingSampler = new ProfilingSampler("FilterEffectOpaque");
            private FilteringSettings __filteringSettings;
            private RenderFilterEffect __renderFilterEffect;
            private Material __material;
            private List<ShaderTagId> __shaderTagIds;

            public OpaqueRenderPass(int layerMask, Material material)
            {
                __filteringSettings = new FilteringSettings(RenderQueueRange.opaque, layerMask);

                __material = material;

                __shaderTagIds = new List<ShaderTagId>(4);
                __shaderTagIds.Add(new ShaderTagId("SRPDefaultUnlit"));
                __shaderTagIds.Add(new ShaderTagId("UniversalForward"));
                __shaderTagIds.Add(new ShaderTagId("UniversalForwardOnly"));
                __shaderTagIds.Add(new ShaderTagId("LightweightForward"));

                renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            }

            public void Init(RenderFilterEffect renderFilterEffect)
            {
                __renderFilterEffect = renderFilterEffect;
            }

            // This method is called before executing the render pass.
            // It can be used to configure render targets and their clear state. Also to create temporary render target textures.
            // When empty this render pass will render to the active camera render target.
            // You should never call CommandBuffer.SetRenderTarget. Instead call <c>ConfigureTarget</c> and <c>ConfigureClear</c>.
            // The render pipeline will ensure target setup and clearing happens in a performant manner.
            /*public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
            }*/

            // Here you can implement the rendering logic.
            // Use <c>ScriptableRenderContext</c> to issue drawing commands or execute command buffers
            // https://docs.unity3d.com/ScriptReference/Rendering.ScriptableRenderContext.html
            // You don't have to call ScriptableRenderContext.submit, the render pipeline will call it at specific points in the pipeline.
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                CommandBuffer cmd = CommandBufferPool.Get();
                using (new ProfilingScope(cmd, __profilingSampler))
                {
                    var drawingSettings = CreateDrawingSettings(__shaderTagIds, ref renderingData, renderingData.cameraData.defaultOpaqueSortFlags);

                    if (__renderFilterEffect != null)
                    {
                        var parameters = __renderFilterEffect.GetParameters(out int length);

                        __material.SetInt(FilterParamsLength, length);

                        if (length > 0)
                        {
                            __material.SetVectorArray(FilterParams, parameters);

                            __material.SetColor(FilterSourceColor, __renderFilterEffect.sourceColor);

                            drawingSettings.overrideMaterial = __material;
                            drawingSettings.overrideMaterialPassIndex = 1;

                            context.ExecuteCommandBuffer(cmd);
                            cmd.Clear();
                        }

                        //if (RenderFilterEffect.activeCount > 0)
                        __material.SetColor(FilterDestinationColor, __renderFilterEffect.destinationColor);
                    }
                    context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref __filteringSettings);
                }

                CommandBufferPool.Release(cmd);
            }

            // Cleanup any allocated resources that were created during the execution of this render pass.
            /*public override void OnCameraCleanup(CommandBuffer cmd)
            {
            }*/
        }

        public LayerMask transparentLayerMask;
        public LayerMask opaqueLayerMask;

        private Material __material;
        private TransparentRenderPass __transparentRenderPass;
        private OpaqueRenderPass __opaquerRenderPass;

        /// <inheritdoc/>
        public override void Create()
        {
            __material = new Material(Shader.Find("ZG/FilterEffectURP"));

            __transparentRenderPass = transparentLayerMask.value == 0 ? null : new TransparentRenderPass(transparentLayerMask);
            __opaquerRenderPass = opaqueLayerMask.value == 0 ? null : new OpaqueRenderPass(opaqueLayerMask, __material);
        }

        // Here you can inject one or multiple render passes in the renderer.
        // This method is called when setting up the renderer once per-camera.
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (RenderFilterEffect.activeCount < 1)
                return;

            var renderFilterEffect = renderingData.cameraData.camera.GetComponent<RenderFilterEffect>();
            if (renderFilterEffect != null)
            {
                renderFilterEffect.enabled = false;

                if(__transparentRenderPass != null)
                    __transparentRenderPass.Init(renderFilterEffect);

                if(__opaquerRenderPass != null)
                    __opaquerRenderPass.Init(renderFilterEffect);
            }

            if (__transparentRenderPass != null)
                // Configures where the render pass should be injected.
                renderer.EnqueuePass(__transparentRenderPass);

            if (__opaquerRenderPass != null)
                renderer.EnqueuePass(__opaquerRenderPass);
        }
    }
}