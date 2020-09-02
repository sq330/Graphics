using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering;

namespace UnityEditor.Rendering.Universal
{
    [Flags]
    enum ShaderFeatures
    {
        MainLight = (1 << 0),
        MainLightShadows = (1 << 1),
        AdditionalLights = (1 << 2),
        AdditionalLightShadows = (1 << 3),
        VertexLighting = (1 << 4),
        SoftShadows = (1 << 5),
        MixedLighting = (1 << 6),
        TerrainHoles = (1 << 7),
        DeferredShading = (1 << 8), // DeferredRenderer is in the list of renderer
        DeferredWithAccurateGbufferNormals = (1 << 9),
        DeferredWithoutAccurateGbufferNormals = (1 << 10),
        ScreenSpaceOcclusion = (1 << 11)
    }
    internal class ShaderPreprocessor : IPreprocessShaders
    {
        public static readonly string kPassNameGBuffer = "GBuffer";
        public static readonly string kTerrainShaderName = "Universal Render Pipeline/Terrain/Lit";
#if PROFILE_BUILD
        private const string k_ProcessShaderTag = "OnProcessShader";
#endif

        ShaderKeyword m_MainLightShadows = new ShaderKeyword(ShaderKeywordStrings.MainLightShadows);
        ShaderKeyword m_AdditionalLightsVertex = new ShaderKeyword(ShaderKeywordStrings.AdditionalLightsVertex);
        ShaderKeyword m_AdditionalLightsPixel = new ShaderKeyword(ShaderKeywordStrings.AdditionalLightsPixel);
        ShaderKeyword m_AdditionalLightShadows = new ShaderKeyword(ShaderKeywordStrings.AdditionalLightShadows);
        ShaderKeyword m_DeferredAdditionalLightShadows = new ShaderKeyword(ShaderKeywordStrings._DEFERRED_ADDITIONAL_LIGHT_SHADOWS);
        ShaderKeyword m_CascadeShadows = new ShaderKeyword(ShaderKeywordStrings.MainLightShadowCascades);
        ShaderKeyword m_SoftShadows = new ShaderKeyword(ShaderKeywordStrings.SoftShadows);
        ShaderKeyword m_MixedLightingSubtractive = new ShaderKeyword(ShaderKeywordStrings.MixedLightingSubtractive);
        ShaderKeyword m_Lightmap = new ShaderKeyword("LIGHTMAP_ON");
        ShaderKeyword m_DirectionalLightmap = new ShaderKeyword("DIRLIGHTMAP_COMBINED");
        ShaderKeyword m_AlphaTestOn = new ShaderKeyword("_ALPHATEST_ON");
        ShaderKeyword m_GbufferNormalsOct = new ShaderKeyword("_GBUFFER_NORMALS_OCT");
        ShaderKeyword m_UseDrawProcedural = new ShaderKeyword(ShaderKeywordStrings.UseDrawProcedural);
        ShaderKeyword m_ScreenSpaceOcclusion = new ShaderKeyword(ShaderKeywordStrings.ScreenSpaceOcclusion);

        int m_TotalVariantsInputCount;
        int m_TotalVariantsOutputCount;

        // Multiple callback may be implemented.
        // The first one executed is the one where callbackOrder is returning the smallest number.
        public int callbackOrder { get { return 0; } }

        bool IsFeatureEnabled(ShaderFeatures featureMask, ShaderFeatures feature)
        {
            return (featureMask & feature) != 0;
        }

        bool StripUnusedPass(ShaderFeatures features, ShaderSnippetData snippetData)
        {
            if (snippetData.passType == PassType.Meta)
                return true;

            if (snippetData.passType == PassType.ShadowCaster)
                if (!IsFeatureEnabled(features, ShaderFeatures.MainLightShadows) && !IsFeatureEnabled(features, ShaderFeatures.AdditionalLightShadows))
                    return true;

            return false;
        }

        bool StripUnusedFeatures(ShaderFeatures features, Shader shader, ShaderSnippetData snippetData, ShaderCompilerData compilerData)
        {
            // strip main light shadows and cascade variants
            if (!IsFeatureEnabled(features, ShaderFeatures.MainLightShadows))
            {
                if (compilerData.shaderKeywordSet.IsEnabled(m_MainLightShadows))
                    return true;

                if (compilerData.shaderKeywordSet.IsEnabled(m_CascadeShadows))
                    return true;
            }

            if (!IsFeatureEnabled(features, ShaderFeatures.SoftShadows) &&
                compilerData.shaderKeywordSet.IsEnabled(m_SoftShadows))
                return true;

            if (compilerData.shaderKeywordSet.IsEnabled(m_MixedLightingSubtractive) &&
                !IsFeatureEnabled(features, ShaderFeatures.MixedLighting))
                return true;

            // No additional light shadows
            bool isAdditionalLightShadow = compilerData.shaderKeywordSet.IsEnabled(m_AdditionalLightShadows);
            if (!IsFeatureEnabled(features, ShaderFeatures.AdditionalLightShadows) && isAdditionalLightShadow)
                return true;

            bool isDeferredAdditionalShadow = compilerData.shaderKeywordSet.IsEnabled(m_DeferredAdditionalLightShadows);
            if (!IsFeatureEnabled(features, ShaderFeatures.AdditionalLightShadows) && isDeferredAdditionalShadow)
                return true;

            // Additional light are shaded per-vertex. Strip additional lights per-pixel and shadow variants
            bool isAdditionalLightPerPixel = compilerData.shaderKeywordSet.IsEnabled(m_AdditionalLightsPixel);
            if (IsFeatureEnabled(features, ShaderFeatures.VertexLighting) &&
                (isAdditionalLightPerPixel || isAdditionalLightShadow))
                return true;

            // No additional lights
            if (!IsFeatureEnabled(features, ShaderFeatures.AdditionalLights) &&
                (isAdditionalLightPerPixel || isAdditionalLightShadow || compilerData.shaderKeywordSet.IsEnabled(m_AdditionalLightsVertex)))
                return true;

            // Screen Space Occlusion
            if (!IsFeatureEnabled(features, ShaderFeatures.ScreenSpaceOcclusion) &&
                compilerData.shaderKeywordSet.IsEnabled(m_ScreenSpaceOcclusion))
                return true;

            return false;
        }

        bool StripUnsupportedVariants(ShaderCompilerData compilerData)
        {
            // Dynamic GI is not supported so we can strip variants that have directional lightmap
            // enabled but not baked lightmap.
            if (compilerData.shaderKeywordSet.IsEnabled(m_DirectionalLightmap) &&
                !compilerData.shaderKeywordSet.IsEnabled(m_Lightmap))
                return true;

            if (compilerData.shaderCompilerPlatform == ShaderCompilerPlatform.GLES20)
            {
                if (compilerData.shaderKeywordSet.IsEnabled(m_CascadeShadows))
                    return true;

                // GLES2 does not support VertexID that is required for full screen draw procedural pass;
                if (compilerData.shaderKeywordSet.IsEnabled(m_UseDrawProcedural))
                    return true;
            }

            return false;
        }

        bool StripInvalidVariants(ShaderCompilerData compilerData)
        {
            bool isMainShadow = compilerData.shaderKeywordSet.IsEnabled(m_MainLightShadows);
            if (!isMainShadow && compilerData.shaderKeywordSet.IsEnabled(m_CascadeShadows))
                return true;

            bool isAdditionalShadow = compilerData.shaderKeywordSet.IsEnabled(m_AdditionalLightShadows);
            if (isAdditionalShadow && !compilerData.shaderKeywordSet.IsEnabled(m_AdditionalLightsPixel))
                return true;

            bool isDeferredAdditionalShadow = compilerData.shaderKeywordSet.IsEnabled(m_DeferredAdditionalLightShadows);
            if (isDeferredAdditionalShadow && !compilerData.shaderKeywordSet.IsEnabled(m_AdditionalLightsPixel))
                return true;

            bool isShadowVariant = isMainShadow || isAdditionalShadow || isDeferredAdditionalShadow;
            if (!isShadowVariant && compilerData.shaderKeywordSet.IsEnabled(m_SoftShadows))
                return true;

            return false;
        }

        bool StripUnused(ShaderFeatures features, Shader shader, ShaderSnippetData snippetData, ShaderCompilerData compilerData)
        {
            if (StripUnusedFeatures(features, shader, snippetData, compilerData))
                return true;

            if (StripInvalidVariants(compilerData))
                return true;

            if (StripUnsupportedVariants(compilerData))
                return true;

            if (StripUnusedPass(features, snippetData))
                return true;

            // Strip terrain holes
            // TODO: checking for the string name here is expensive
            // maybe we can rename alpha clip keyword name to be specific to terrain?
            if (compilerData.shaderKeywordSet.IsEnabled(m_AlphaTestOn) &&
                !IsFeatureEnabled(features, ShaderFeatures.TerrainHoles) &&
                shader.name.Contains(kTerrainShaderName))
                return true;

            // TODO: Test against lightMode tag instead.
            if (snippetData.passName == kPassNameGBuffer)
            {
                if (!IsFeatureEnabled(features, ShaderFeatures.DeferredShading))
                    return true;
                if (IsFeatureEnabled(features, ShaderFeatures.DeferredWithAccurateGbufferNormals) && !compilerData.shaderKeywordSet.IsEnabled(m_GbufferNormalsOct))
                    return true;
                if (IsFeatureEnabled(features, ShaderFeatures.DeferredWithoutAccurateGbufferNormals) && compilerData.shaderKeywordSet.IsEnabled(m_GbufferNormalsOct))
                    return true;
            }
            return false;
        }

        void LogShaderVariants(Shader shader, ShaderSnippetData snippetData, ShaderVariantLogLevel logLevel, int prevVariantsCount, int currVariantsCount)
        {
            if (logLevel == ShaderVariantLogLevel.AllShaders || shader.name.Contains("Universal Render Pipeline"))
            {
                float percentageCurrent = (float)currVariantsCount / (float)prevVariantsCount * 100f;
                float percentageTotal = (float)m_TotalVariantsOutputCount / (float)m_TotalVariantsInputCount * 100f;

                string result = string.Format("STRIPPING: {0} ({1} pass) ({2}) -" +
                        " Remaining shader variants = {3}/{4} = {5}% - Total = {6}/{7} = {8}%",
                        shader.name, snippetData.passName, snippetData.shaderType.ToString(), currVariantsCount,
                        prevVariantsCount, percentageCurrent, m_TotalVariantsOutputCount, m_TotalVariantsInputCount,
                        percentageTotal);
                Debug.Log(result);
            }
        }

        public void OnProcessShader(Shader shader, ShaderSnippetData snippetData, IList<ShaderCompilerData> compilerDataList)
        {
#if PROFILE_BUILD
            Profiler.BeginSample(k_ProcessShaderTag);
#endif
            UniversalRenderPipelineAsset urpAsset = GraphicsSettings.renderPipelineAsset as UniversalRenderPipelineAsset;
            if (urpAsset == null || compilerDataList == null || compilerDataList.Count == 0)
                return;

            int prevVariantCount = compilerDataList.Count;

            var inputShaderVariantCount = compilerDataList.Count;
            for (int i = 0; i < inputShaderVariantCount;)
            {

                bool removeInput = StripUnused(ShaderBuildPreprocessor.supportedFeatures, shader, snippetData, compilerDataList[i]);
                if (removeInput)
                    compilerDataList[i] = compilerDataList[--inputShaderVariantCount];
                else
                    ++i;
            }

            if(compilerDataList is List<ShaderCompilerData> inputDataList)
                inputDataList.RemoveRange(inputShaderVariantCount, inputDataList.Count - inputShaderVariantCount);
            else
            {
                for(int i = compilerDataList.Count -1; i >= inputShaderVariantCount; --i)
                    compilerDataList.RemoveAt(i);
            }

            if (urpAsset.shaderVariantLogLevel != ShaderVariantLogLevel.Disabled)
            {
                m_TotalVariantsInputCount += prevVariantCount;
                m_TotalVariantsOutputCount += compilerDataList.Count;
                LogShaderVariants(shader, snippetData, urpAsset.shaderVariantLogLevel, prevVariantCount, compilerDataList.Count);
            }
#if PROFILE_BUILD
            Profiler.EndSample();
#endif
        }
    }
    class ShaderBuildPreprocessor : IPreprocessBuildWithReport
#if PROFILE_BUILD
        , IPostprocessBuildWithReport
#endif
    {
        public static ShaderFeatures supportedFeatures
        {
            get {
                if (_supportedFeatures <= 0)
                {
                    FetchAllSupportedFeatures();
                }
                return _supportedFeatures;
            }
        }

        private static ShaderFeatures _supportedFeatures = 0;
        public int callbackOrder { get { return 0; } }
#if PROFILE_BUILD
        public void OnPostprocessBuild(BuildReport report)
        {
            Profiler.enabled = false;
        }
#endif

        public void OnPreprocessBuild(BuildReport report)
        {
            FetchAllSupportedFeatures();
#if PROFILE_BUILD
            Profiler.enableBinaryLog = true;
            Profiler.logFile = "profilerlog.raw";
            Profiler.enabled = true;
#endif
        }

        private static void FetchAllSupportedFeatures()
        {
            List<UniversalRenderPipelineAsset> urps = new List<UniversalRenderPipelineAsset>();
            urps.Add(GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset);
            for(int i = 0; i < QualitySettings.names.Length; i++)
            {
                urps.Add(QualitySettings.GetRenderPipelineAssetAt(i) as UniversalRenderPipelineAsset);
            }

            // Must reset flags.
            _supportedFeatures = 0;
            foreach (UniversalRenderPipelineAsset urp in urps)
            {
                if (urp != null)
                {
                    _supportedFeatures |= GetSupportedShaderFeatures(urp);
                }
            }
        }

        private static ShaderFeatures GetSupportedShaderFeatures(UniversalRenderPipelineAsset pipelineAsset)
        {
            ShaderFeatures shaderFeatures;
            shaderFeatures = ShaderFeatures.MainLight;

            if (pipelineAsset.supportsMainLightShadows)
                shaderFeatures |= ShaderFeatures.MainLightShadows;

            if (pipelineAsset.additionalLightsRenderingMode == LightRenderingMode.PerVertex)
            {
                shaderFeatures |= ShaderFeatures.AdditionalLights;
                shaderFeatures |= ShaderFeatures.VertexLighting;
            }
            else if (pipelineAsset.additionalLightsRenderingMode == LightRenderingMode.PerPixel)
            {
                shaderFeatures |= ShaderFeatures.AdditionalLights;

                if (pipelineAsset.supportsAdditionalLightShadows)
                    shaderFeatures |= ShaderFeatures.AdditionalLightShadows;
            }

            bool anyShadows = pipelineAsset.supportsMainLightShadows ||
                              (shaderFeatures & ShaderFeatures.AdditionalLightShadows) != 0;
            if (pipelineAsset.supportsSoftShadows && anyShadows)
                shaderFeatures |= ShaderFeatures.SoftShadows;

            if (pipelineAsset.supportsMixedLighting)
                shaderFeatures |= ShaderFeatures.MixedLighting;

            if (pipelineAsset.supportsTerrainHoles)
                shaderFeatures |= ShaderFeatures.TerrainHoles;

            bool hasScreenSpaceOcclusion = false;
            bool hasDeferredRenderer = false;
            bool withAccurateGbufferNormals = false;
            bool withoutAccurateGbufferNormals = false;

            int rendererCount = pipelineAsset.m_RendererDataList.Length;
            for (int rendererIndex = 0; rendererIndex < rendererCount; ++rendererIndex)
            {
                ScriptableRenderer renderer = pipelineAsset.GetRenderer(rendererIndex);
                if (renderer is ForwardRenderer)
                {
                    ForwardRenderer forwardRenderer = (ForwardRenderer)renderer;
                    if (forwardRenderer.renderingMode == RenderingMode.Deferred || forwardRenderer.mutableRenderingMode)
                    {
                        hasDeferredRenderer |= true;
                        withAccurateGbufferNormals |= forwardRenderer.accurateGbufferNormals;
                        withoutAccurateGbufferNormals |= !forwardRenderer.accurateGbufferNormals;
                    }
                }

                // Check for Screen Space Ambient Occlusion Renderer Feature
                ScriptableRendererData rendererData = pipelineAsset.m_RendererDataList[rendererIndex];
                if (rendererData != null)
                {
                    for (int rendererFeatureIndex = 0; rendererFeatureIndex < rendererData.rendererFeatures.Count; rendererFeatureIndex++)
                    {
                        ScriptableRendererFeature rendererFeature = rendererData.rendererFeatures[rendererFeatureIndex];
                        ScreenSpaceAmbientOcclusion ssao = rendererFeature as ScreenSpaceAmbientOcclusion;
                        hasScreenSpaceOcclusion |= ssao != null;
                    }
                }
            }

            if (hasDeferredRenderer)
                shaderFeatures |= ShaderFeatures.DeferredShading;

            // We can only strip accurateGbufferNormals related variants if all DeferredRenderers use the same option.
            if (withAccurateGbufferNormals && !withoutAccurateGbufferNormals)
                shaderFeatures |= ShaderFeatures.DeferredWithAccurateGbufferNormals;

            if (!withAccurateGbufferNormals && withoutAccurateGbufferNormals)
                shaderFeatures |= ShaderFeatures.DeferredWithoutAccurateGbufferNormals;

            if (hasScreenSpaceOcclusion)
                shaderFeatures |= ShaderFeatures.ScreenSpaceOcclusion;

            return shaderFeatures;
        }
    }
}
