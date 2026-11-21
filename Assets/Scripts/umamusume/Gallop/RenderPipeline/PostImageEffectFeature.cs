using System;
using System.Collections.Generic;
using System.Reflection;
using Gallop.ImageEffect;
using Gallop.Live;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Gallop.RenderPipeline
{
    /// <summary>
    /// Recovered SourceSetup -> Dof/Diffusion/Bloom/Overlay -> RadialBlur -> SimpleColorCorrection -> FinalBlit chain.
    /// Other reference passes are not implemented by this feature yet.
    /// </summary>
    public class PostImageEffectFeature : ScriptableRendererFeature
    {
        //(Grabación del live) Referencia a la instancia activa. La usa el grabador offline para
        //poder desactivar/activar los postefectos del show sin depender de AssetDatabase.
        public static PostImageEffectFeature Instance { get; private set; }

        private const int INVALID_NAME_ID = -2;
        private const int REGISTER_PASS_SIZE = 12;
        private const string SOURCE_SETUP_POOL_NAME =
            "PostImageEffectFeature.SourceSetup";
        private const string FINAL_BLIT_POOL_NAME =
            "PostImageEffectFeature.FinalBlit";

        // 只保留 DofDiffusionBloomOverlayPass 实际需要的 Pass。
        private SourceSetupPass _sourceSetupPass;
        private BlurOptimizePass _blurOptimizePass;
        private DofDiffusionBloomOverlayPass
            _dofDiffusionBloomOverlayPass;
        private FinalBlitPass _finalBlitPass;
        private RadialBlurPass _radialBlurPass;
        private ChromaticAberrationPass _chromaticAberrationPass;
        private TiltShiftPass _tiltShiftPass;
        private GlobalFogPass _globalFogPass;
        private DepthOnlyPass _depthOnlyPass;
        private DepthReleasePass _depthReleasePass;
        private static readonly int DepthTextureId = Shader.PropertyToID("_CameraDepthTexture");
        private VortexPass _vortexPass;
        private LensDistortionPass _lensDistortionPass;
        private HatchingPass _hatchingPass;
        private FluctuationPass _fluctuationPass;
        private SimpleColorCorrectionPass _colorCorrectionPass;
        private FinalBlitPass _colorEarlyResolvePass;
        private ExposureToneCurvePass _exposurePass, _toneCurvePass;
        private SunShaftsPass _sunShaftsPass;
        private IndirectLightShaftsPass _indirectLightShaftsPass;
        private TransmittedLightPass _transmittedLightPass;
        // True when DepthOnlyPass is scheduled for this camera, i.e. `_CameraDepthTexture` (property id 0xdb) holds the
        // DepthOnlyPass depth (opaque + DepthOnlyCubes) while the post chain runs. TransmittedLightPass renders its mask
        // against that texture (JP DrawTransmittedLightMask 0x7ffd595ce920).
        public bool DepthTextureReady { get; private set; }
        private LensFlareRenderPass _lensFlarePass;
        private MultiCameraCompositePass _multiCameraCompositePass;
        private MultiCameraFinalCompositePass _multiCameraFinalCompositePass;
        private ProjectorRenderPass _projectorPass;
        private MonitorScreenCapturePass _monitorCapturePass;

        private RenderTextureHandle _sourceRT;
        private List<ScriptableRenderPass> _registerPass;
        private bool _loggedParameterMissing;
        private bool _loggedParameterDisabled;
        private bool _loggedSetupFailed;
        private bool _loggedBypass;
        private bool _shaderLoadRequested;
        private float _nextShaderLoadAttempt;

        // Missing shader resources cause Setup to fail without replacing camera color.
        // Disable this switch for an unprocessed A/B baseline.
        [SerializeField]
        private bool _enableRendering = true;

        // Unlike SetActive, this leaves the mandatory multi-camera composition passes scheduled.
        public bool EnableRendering
        {
            get => _enableRendering;
            set => _enableRendering = value;
        }

        // Active-camera publication alias, not storage shared by every camera.
        // GallopImageEffect / MultiCamera retain their own CameraData parameters.
        private static Parameter _runtimeParameter = new Parameter();

        public static Parameter RuntimeParameter
        {
            get
            {
                if (_runtimeParameter == null)
                    _runtimeParameter = new Parameter();
                return _runtimeParameter;
            }
            internal set => _runtimeParameter = value;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetRuntimeParameter()
        {
            _runtimeParameter = new Parameter();
        }

        public RenderTextureHandle SourceRT
        {
            get { return _sourceRT; }
        }

        // Pure scheduling policy, shared with regression tests. URP performs the stable
        // event sort; this does not change the existing DOF pass or its late camera writes.
        // Lens flare at the JP event (600, after the 550 chain) instead of the DOF event. Static so it can be
        // toggled at runtime while comparing frames (e.g. live 1053 f1331).
        public static bool FlareAfterChain = true;
        // Vortex (603) and ColorCorrection (604) at their JP events, with a late resolve blit at 605.
        public static bool JpPostEvents = true;
        private FinalBlitPass _lateResolvePass;

        public struct ColorChainSchedule
        {
            public int FirstEvent, LastEvent;
            public bool EarlyResolve, FinalResolve;
        }
        public static ColorChainSchedule GetColorChainSchedule(bool dof, bool radial, bool color, RenderPassEvent effectEvent)
        {
            bool existing = dof || radial;
            int first = existing ? (int)effectEvent : 550;
            int last = first;
            if (color) { first = System.Math.Min(first, 550); last = System.Math.Max(last, 550); }
            return new ColorChainSchedule {
                FirstEvent = first, LastEvent = last,
                EarlyResolve = color && existing && (int)effectEvent > 550,
                FinalResolve = radial || (color && (!existing || (int)effectEvent <= 550)) ||
                    (existing && (int)effectEvent <= 550)
            };
        }

        public override void Create()
        {
            Instance = this;
            Dispose();
            _loggedParameterMissing = false;
            _loggedParameterDisabled = false;
            _loggedSetupFailed = false;
            _loggedBypass = false;
            _shaderLoadRequested = false;
            _nextShaderLoadAttempt = 0f;

            RenderPassEvent passEvent =
                RenderPassEvent.BeforeRenderingPostProcessing;

            _sourceSetupPass = new SourceSetupPass(this, passEvent);
            _dofDiffusionBloomOverlayPass = new DofDiffusionBloomOverlayPass(passEvent);
            _finalBlitPass = new FinalBlitPass(this, passEvent);
            _radialBlurPass = new RadialBlurPass(passEvent);
            _chromaticAberrationPass = new ChromaticAberrationPass(passEvent);
            _tiltShiftPass = new TiltShiftPass(passEvent);
            _globalFogPass = new GlobalFogPass(passEvent);
            // GallopRenderer ctor: DepthOnlyPass(0x96, RenderQueueRange.opaque, 0xffffffff), DepthReleasePass(0x96).
            _depthOnlyPass = new DepthOnlyPass((RenderPassEvent)0x96, RenderQueueRange.opaque, -1);
            _depthReleasePass = new DepthReleasePass((RenderPassEvent)0x96);
            _vortexPass = new VortexPass(passEvent);
            _lensDistortionPass = new LensDistortionPass(passEvent);
            _hatchingPass = new HatchingPass(passEvent);
            _fluctuationPass = new FluctuationPass(passEvent);
            _colorCorrectionPass = new SimpleColorCorrectionPass();
            _lateResolvePass = new FinalBlitPass(this, (RenderPassEvent)605, "LateResolve");
            _colorEarlyResolvePass = new FinalBlitPass(this, passEvent, "ColorEarlyResolve");
            _exposurePass = new ExposureToneCurvePass(false);
            _toneCurvePass = new ExposureToneCurvePass(true);
            _sunShaftsPass = new SunShaftsPass(passEvent);
            _indirectLightShaftsPass = new IndirectLightShaftsPass(passEvent);
            _transmittedLightPass = new TransmittedLightPass(passEvent);
            _lensFlarePass = new LensFlareRenderPass(passEvent);
            _multiCameraCompositePass = new MultiCameraCompositePass();
            _multiCameraFinalCompositePass = new MultiCameraFinalCompositePass();
            _projectorPass = new ProjectorRenderPass();
            _monitorCapturePass = new MonitorScreenCapturePass((RenderPassEvent)650);
            // PostImageEffectFeature.Create: BlurOptimizePass(0x226).
            _blurOptimizePass = new BlurOptimizePass((RenderPassEvent)0x226);

            _registerPass = new List<ScriptableRenderPass>(REGISTER_PASS_SIZE);

            _sourceRT = RenderTextureHandle.Make(new RenderTargetIdentifier(INVALID_NAME_ID), 0, 0);

            // 让 Dof Pass 直接使用同一套 CameraData 参数解析逻辑。
            DofDiffusionBloomOverlayPass.FeatureParameterResolver = ResolveFeatureParameter;
        }

        public override void AddRenderPasses( ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            Camera camera = renderingData.cameraData.camera;
            if (camera == null)
                return;

            var director = Gallop.Live.Director.instance;
            // Native ProjectorFeature draws stage projectors after opaque geometry,
            // independently of presentation-camera post-processing (including reflection/monitor views).
            if (Application.isPlaying && camera.cameraType == CameraType.Game && director != null &&
                _projectorPass != null && _projectorPass.Setup(director.LightProjectors, director.MirrorBallProjectors))
            {
                renderer.EnqueuePass(_projectorPass);
            }

            if (Gallop.Live.MonitorCamera.IsMonitorCamera(camera))
                return;

            // The timeline parameter is authored for the Live presentation
            // cameras. Reflection/monitor cameras use different render targets
            // and depth ranges; running the same multi-RT DOF/Bloom chain on
            // them corrupts the reflection texture and can leak black holes or
            // a white veil into the main frame.
            // [REPORT §10.3-B1/§4.2] 原生按相机组逐台注册后处理（RegisterPass 走
            // camera stack，覆盖演示相机组），master7 原以 camera != MainRenderCamera
            // 的引用相等做门控：CameraSwitcher 切换瞬间旧相机当帧不注册、新相机
            // 重走 Setup/SourceRT 重建链，切换帧丢失一次后处理输出。改为对
            // Director 的演示相机组做成员判定（IsPresentationCamera），同帧内
            // 组内每台相机都注册整链，消除切换窗口。非 Game 类型和
            // MonitorCameraCapture 的跳过保持不变（与原生名字跳过一致）。
            if (!Application.isPlaying || camera.cameraType != CameraType.Game || director == null)
                return;
            camera.TryGetComponent<MultiCamera>(out var multiCamera);
            if (multiCamera == null && !director.IsPresentationCamera(camera))
            {
                // The stage mirror camera is not a presentation camera, but the game still runs
                // SetSourceRTPass -> BlurOptimizePass on it (CameraData.ImageEffectParameter.BlurOptimized,
                // written by MirrorReflection). Only that reduced chain is registered here.
                EnqueueMirrorBlur(renderer, camera);
                return;
            }

            // Native composition runs even when the optional image-effect chain is disabled.
            if (multiCamera != null)
            {
                if (_multiCameraCompositePass != null && _multiCameraCompositePass.Setup(multiCamera))
                    renderer.EnqueuePass(_multiCameraCompositePass);
            }
            else if (camera == director.MainRenderCamera && _multiCameraFinalCompositePass != null &&
                _multiCameraFinalCompositePass.Setup(director.MultiCameraFinalComposite))
                renderer.EnqueuePass(_multiCameraFinalCompositePass);

            // Game captures show the stage monitors displaying the main camera image (a face close-up at f1236 of
            // live 1001) while the MonitorCamera is inactive: the screen texture holds the presentation camera's
            // final image whenever no monitor camera is rendering into it.
            if (multiCamera == null && camera == director.MainRenderCamera && _monitorCapturePass != null &&
                _monitorCapturePass.Setup(director))
                renderer.EnqueuePass(_monitorCapturePass);

            bool renderEnabled = _enableRendering;
            if (!renderEnabled)
            {
                if (!_loggedBypass)
                {
                    _loggedBypass = true;
                }
                EnqueueFlareOnly(renderer);
                return;
            }

            // Renderer features are constructed while the pipeline is being
            // deserialized, before UmaViewerMain.Start() has loaded shader.a.
            // Request the bundle here as soon as a Live camera actually needs
            // the recovered chain. Setup below is retried on subsequent frames
            // and will create the materials after the synchronous load lands.
            if (!ShaderManager.IsShaderBundleReady &&
                Time.realtimeSinceStartup >= _nextShaderLoadAttempt)
            {
                _nextShaderLoadAttempt = Time.realtimeSinceStartup + 1f;
                ShaderManager.LoadShaderBundle();
                if (!_shaderLoadRequested)
                {
                    _shaderLoadRequested = true;
                }
            }

            CameraData cameraData;
            if (!camera.TryGetComponent(out cameraData))
                cameraData = camera.gameObject.AddComponent<CameraData>();

            // Bind this camera's owned state before deciding draw type.
            // Live auto-disable must never leak through the active-camera alias.
            var imageEffect = camera.GetComponent<GallopImageEffect>();
            var cameraParameter = multiCamera != null ? multiCamera.PostEffectParameter
                : imageEffect != null ? imageEffect.RenderParameter : RuntimeParameter;
            cameraData.Parameter = cameraParameter;
            cameraParameter.TargetCamera = camera;
            cameraData.UpdateImageEffectParameter();

            Parameter parameter = ResolveFeatureParameter(cameraData);
            if (parameter == null)
            {
                if (!_loggedParameterMissing)
                {
                    _loggedParameterMissing = true;
                    Debug.LogWarning($"[RecoveredPostEffect] skip camera={camera.name}: CameraData.Parameter missing");
                }
                EnqueueFlareOnly(renderer);
                return;
            }

            if (!parameter.IsEnable)
            {
                if (!_loggedParameterDisabled)
                {
                    _loggedParameterDisabled = true;
                }
                EnqueueFlareOnly(renderer);
                return;
            }

            if (_registerPass == null)
            {
                _registerPass =  new List<ScriptableRenderPass>(REGISTER_PASS_SIZE);
            }

            _registerPass.Clear();

            if (_sourceSetupPass == null || _dofDiffusionBloomOverlayPass == null || _finalBlitPass == null)
            {
                return;
            }

            bool dofReady = _dofDiffusionBloomOverlayPass.Setup(cameraData, this);
            bool radialReady = Gallop.RenderPipeline.PostEffectSelection.IsEnabled(PostEffectId.RadialBlur) &&
                _radialBlurPass != null && _radialBlurPass.Setup(parameter.RadialBlur, this);
            bool chromaticReady = Gallop.RenderPipeline.PostEffectSelection.IsEnabled(PostEffectId.ChromaticAberration) &&
                                  _chromaticAberrationPass != null &&
                                  _chromaticAberrationPass.Setup(parameter.ChromaticAberration, this);
            bool tiltReady = Gallop.RenderPipeline.PostEffectSelection.IsEnabled(PostEffectId.TiltShift) &&
                _tiltShiftPass != null && _tiltShiftPass.Setup(parameter.TiltShift, this);
            bool fogReady = Gallop.RenderPipeline.PostEffectSelection.IsEnabled(PostEffectId.GlobalFog) &&
                _globalFogPass != null && _globalFogPass.Setup(parameter.GlobalFog, this);
            bool vortexReady = Gallop.RenderPipeline.PostEffectSelection.IsEnabled(PostEffectId.Vortex) &&
                _vortexPass != null && _vortexPass.Setup(parameter.Vortex, this);
            bool hatchingReady = Gallop.RenderPipeline.PostEffectSelection.IsEnabled(PostEffectId.Hatching) &&
                _hatchingPass != null && _hatchingPass.Setup(parameter.Hatching, this);
            bool lensDistortionReady =Gallop.RenderPipeline.PostEffectSelection.IsEnabled(PostEffectId.LensDistortion) &&
                _lensDistortionPass != null && _lensDistortionPass.Setup(parameter.LensDistortion, this);
            bool fluctuationReady =Gallop.RenderPipeline.PostEffectSelection.IsEnabled(PostEffectId.Fluctuation) &&
                                    _fluctuationPass != null &&
                                    _fluctuationPass.Setup(parameter.Fluctuation, this);
            bool colorReady = Gallop.RenderPipeline.PostEffectSelection.IsEnabled(PostEffectId.ColorCorrection) &&
                _colorCorrectionPass != null && _colorCorrectionPass.Setup(parameter.ColorCorrection, this);
            bool exposureReady = Gallop.RenderPipeline.PostEffectSelection.IsEnabled(PostEffectId.Exposure) &&
                _exposurePass != null && _exposurePass.Setup(parameter.ExposureParam, parameter.ToneCurveParam, this);
            bool toneReady = Gallop.RenderPipeline.PostEffectSelection.IsEnabled(PostEffectId.ToneCurve) &&
                _toneCurvePass != null && _toneCurvePass.Setup(parameter.ExposureParam, parameter.ToneCurveParam, this);
            bool sunReady = Gallop.RenderPipeline.PostEffectSelection.IsEnabled(PostEffectId.SunShafts) &&
                _sunShaftsPass != null && _sunShaftsPass.Setup(parameter.SunShafts, this);
            bool shaftsReady = Gallop.RenderPipeline.PostEffectSelection.IsEnabled(PostEffectId.LightShafts) &&
                _indirectLightShaftsPass != null && _indirectLightShaftsPass.Setup(parameter.LightShafts, this);
            // Glow of the stage lights (TransmittedLight track); scheduled like the shafts.
            bool transmittedReady = Gallop.RenderPipeline.PostEffectSelection.IsEnabled(PostEffectId.TransmittedLight) &&
                _transmittedLightPass != null &&
                _transmittedLightPass.Setup(Gallop.Live.Cutt.LiveTransmittedLightTimeline.Current, this);
            bool correctionReady = colorReady || exposureReady || toneReady;
            // TransmittedLight composes like the shafts: same scheduling and limiter layer.
            // Lens flares: drawn by the native LensFlareRenderPass after DOF/bloom/chromatic and before
            // the colour correction (see LensFlareRenderPass).
            bool flareReady = _lensFlarePass != null && _lensFlarePass.Setup(this, false);
            bool lightReady = sunReady || shaftsReady || transmittedReady || flareReady;
            if (!dofReady && !radialReady && !chromaticReady && !tiltReady && !fogReady && !vortexReady && !hatchingReady && !lensDistortionReady && !fluctuationReady && !correctionReady && !lightReady)
            {
                if (!_loggedSetupFailed)
                {
                    _loggedSetupFailed = true;
                    Debug.LogWarning($"[RecoveredPostEffect] setup failed camera={camera.name} " +
                                     $"type={parameter.DofDiffuionBloomOverlay.UseDofDiffusionBloomType} " +
                                     $"shaderReady={ShaderManager.IsShaderBundleReady}");
                }
                return;
            }

            // The client never copies URP's depth: when any effect requests the depth texture its GallopRenderer
            // schedules DepthOnlyPass (event 0x96, SHADOWCASTER pass of the opaque renderers) and publishes the
            // result as _CameraDepthTexture (see DepthOnlyPass). So URP's own depth copy / prepass is switched
            // off for this camera and the effects no longer ask URP for depth (no ConfigureInput(Depth)).
            var camData = camera.GetUniversalAdditionalCameraData();
            if (camData != null && camData.requiresDepthTexture)
                camData.requiresDepthTexture = false;

            // GetRenderPassInputs: any pass that declares a Depth input. The depth inputs of the passes are
            // the effects that read _CameraDepthTexture: DOF/CoC, radial blur (depth), GlobalFog, Vortex,
            // Exposure / ToneCurve.
            bool depthNeeded = parameter.IsUseDepthTexture || fogReady || vortexReady || exposureReady || toneReady ||
                               (radialReady && parameter.RadialBlur.IsEnabledDepth);
            DepthTextureReady = depthNeeded && _depthOnlyPass != null && _depthReleasePass != null;
            if (depthNeeded && _depthOnlyPass != null && _depthReleasePass != null)
            {
                _depthOnlyPass.ResetState();
                _depthOnlyPass.Setup(0xffffffffu, DepthTextureId, renderingData.cameraData.cameraTargetDescriptor);
                renderer.EnqueuePass(_depthOnlyPass);
                _depthReleasePass.Setup(DepthTextureId);
                renderer.EnqueuePass(_depthReleasePass);
            }

            RenderPassEvent effectEvent = parameter.DofDiffuionBloomOverlay.RenderPassEvent;

            // Color keeps its native default event550, independent of DOF's event.
            // JP: LensFlareRenderPass.Setup sets its event to 600, after the 550 chain, so it must not shape the
            // chain schedule when FlareAfterChain is on.
            bool chainLightReady = FlareAfterChain ? (sunReady || shaftsReady || transmittedReady) : lightReady;
            // With JpPostEvents the ColorCorrection runs at 604, outside the 550 chain.
            bool chainCorrectionReady = JpPostEvents ? (exposureReady || toneReady) : correctionReady;
            var schedule = GetColorChainSchedule(dofReady, radialReady,
                chainCorrectionReady || chainLightReady, effectEvent);
            _sourceSetupPass.renderPassEvent = (RenderPassEvent)schedule.FirstEvent;
            _finalBlitPass.renderPassEvent = (RenderPassEvent)schedule.LastEvent;

            // 同一 RenderPassEvent 下按 Enqueue 顺序执行。
            _registerPass.Add(_sourceSetupPass);
            // Native GallopImageEffect order: GlobalFog before SunShafts and the light shafts.
            if (fogReady)
            {
                _globalFogPass.renderPassEvent = effectEvent;
                _registerPass.Add(_globalFogPass);
            }
            // JP RegisterPass: GlobalFog -> SunShafts -> TransmittedLight -> IndirectLightShafts -> ... -> DOF.
            if (sunReady) _registerPass.Add(_sunShaftsPass);
            if (transmittedReady) _registerPass.Add(_transmittedLightPass);
            if (shaftsReady) _registerPass.Add(_indirectLightShaftsPass);
            if (dofReady) _registerPass.Add(_dofDiffusionBloomOverlayPass);
            // Native GallopImageEffect chain order: DOF/Diffusion/Bloom -> TiltShift -> RadialBlur.
            if (tiltReady)
            {
                _tiltShiftPass.renderPassEvent = effectEvent;
                _registerPass.Add(_tiltShiftPass);
            }
            // JP RegisterPass: TiltShift -> Hatching -> RadialBlur.
            if (hatchingReady)
            {
                _hatchingPass.renderPassEvent = effectEvent;
                _registerPass.Add(_hatchingPass);
            }
            if (radialReady)
            {
                _radialBlurPass.renderPassEvent = effectEvent;
                _registerPass.Add(_radialBlurPass);
            }
            // Native GallopImageEffect order: RadialBlur -> Fluctuation -> LensDistortion -> CA.
            if (fluctuationReady)
            {
                _fluctuationPass.renderPassEvent = effectEvent;
                _registerPass.Add(_fluctuationPass);
            }
            // JP RegisterPass: Fluctuation -> LensDistortion -> ChromaticAberration.
            if (lensDistortionReady)
            {
                _lensDistortionPass.renderPassEvent = effectEvent;
                _registerPass.Add(_lensDistortionPass);
            }
            if (chromaticReady)
            {
                _chromaticAberrationPass.renderPassEvent = effectEvent;
                _registerPass.Add(_chromaticAberrationPass);
            }
            if (flareReady)
            {
                _lensFlarePass.renderPassEvent = FlareAfterChain ? (RenderPassEvent)600 : effectEvent;
                _registerPass.Add(_lensFlarePass);
            }
            if (toneReady) _registerPass.Add(_toneCurvePass);
            if (exposureReady) _registerPass.Add(_exposurePass);
            // JP: Vortex 603 (LiveImageEffect.Initialize) and ColorCorrection 604 (Director.OnUpdateColorCorrection)
            // run after the 550 chain and the 600 lens flare, on the resolved camera colour (each pass starts
            // with ResetSourceRT); the late resolve at 605 copies the last temporary back.
            if (colorReady)
            {
                _colorCorrectionPass.renderPassEvent = JpPostEvents ? (RenderPassEvent)604 : RenderPassEvent.BeforeRenderingPostProcessing;
                _registerPass.Add(_colorCorrectionPass);
            }
            // Native Vortex lives in LiveImageEffect's after-lensflare command buffer: last.
            if (vortexReady)
            {
                _vortexPass.renderPassEvent = JpPostEvents ? (RenderPassEvent)603 : effectEvent;
                _registerPass.Add(_vortexPass);
            }
            if (JpPostEvents && (colorReady || vortexReady) && _lateResolvePass != null)
                _registerPass.Add(_lateResolvePass);
            // All native event550 corrections must resolve before a late DOF draw.
            if (schedule.EarlyResolve)
            {
                _registerPass.Add(_colorEarlyResolvePass);
            }

            // Radial always hands its temporary result back to this feature.
            // DOF alone writes directly to camera color for events after 550.
            if (schedule.FinalResolve)
            {
                _registerPass.Add(_finalBlitPass);
            }

            for (int i = 0; i < _registerPass.Count; i++)
                renderer.EnqueuePass(_registerPass[i]);

            // Read-only runtime evidence: this records the actual URP descriptor
            // and scheduled pass count without changing the render chain.
            LiveRuntimeDiagnostics.RecordRenderPass(
                camera, renderingData.cameraData.cameraTargetDescriptor, parameter,
                _registerPass.Count, ShaderManager.IsShaderBundleReady,
                renderingData.cameraData.postProcessEnabled);
        }

        // Native PostImageEffectFeature.RegisterPass: SetSourceRTPass, then BlurOptimizePass.Setup(cameraData, feature)
        // (JP 0x7ffd595c6ca0 -> 0x7ffd595d95c0). The blur works in place on the camera colour (SourceRT).
        private void EnqueueMirrorBlur(ScriptableRenderer renderer, Camera camera)
        {
            if (_blurOptimizePass == null || _sourceSetupPass == null || renderer == null) return;
            if (!Application.isPlaying || camera.cameraType != CameraType.Game) return;
            var mirror = camera.GetComponentInParent<Gallop.MirrorReflection>();
            if (mirror == null || mirror.MirrorCamera != camera) return;
            if (!camera.TryGetComponent(out CameraData mirrorData)) return;
            if (!_blurOptimizePass.Setup(mirrorData, this)) return;
            _sourceSetupPass.renderPassEvent = _blurOptimizePass.renderPassEvent;
            renderer.EnqueuePass(_sourceSetupPass);
            renderer.EnqueuePass(_blurOptimizePass);
        }

        // The native LensFlareRenderPass is independent of the image-effect chain: when the chain does not
        // run for a presentation camera the flares are still drawn, straight onto the camera colour.
        private void EnqueueFlareOnly(ScriptableRenderer renderer)
        {
            if (_lensFlarePass == null || renderer == null) return;
            if (!_lensFlarePass.Setup(this, true)) return;
            _lensFlarePass.renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            renderer.EnqueuePass(_lensFlarePass);
        }

        /// <summary>
        /// 将当前源设置为相机颜色目标
        /// 这替代原版SetSourceRTPass对 Dof-only 链路的作用
        /// </summary>
        public void ResetSourceRT( CommandBuffer cmd, ref RenderingData renderingData)
        {
            ScriptableRenderer renderer = renderingData.cameraData.renderer;

            if (renderer == null)
                return;

            RTHandle cameraColorHandle = renderer.cameraColorTargetHandle;
            if (cameraColorHandle == null)
                return;

            RenderTargetIdentifier cameraColorTarget = cameraColorHandle.nameID;

            RenderTextureDescriptor descriptor = renderingData.cameraData.cameraTargetDescriptor;

            if (cmd != null && !_sourceRT.RtId.Equals(cameraColorTarget) && _sourceRT.NameId != INVALID_NAME_ID)
            {
                cmd.ReleaseTemporaryRT(_sourceRT.NameId);
            }

            _sourceRT = RenderTextureHandle.Make( cameraColorTarget, descriptor.width, descriptor.height);
        }

        /// <summary>
        /// DofDiffusionBloomOverlayPass.Execute 在完成后调用此函数，
        /// 把输出临时 RT 交回 Feature。
        /// </summary>
        public void SetSourceRT(RenderTextureHandle sourceHandle, CommandBuffer cmd, ref RenderingData renderingData)
        {
            if (_sourceRT.NameId == sourceHandle.NameId)
                return;

            ReleaseCurrentTemporaryRT(cmd, ref renderingData);

            _sourceRT = sourceHandle;
        }

        private void ReleaseCurrentTemporaryRT(CommandBuffer cmd, ref RenderingData renderingData)
        {
            ScriptableRenderer renderer = renderingData.cameraData.renderer;

            if (renderer == null)
                return;

            RTHandle cameraColorHandle = renderer.cameraColorTargetHandle;
            if (cameraColorHandle == null)
                return;

            RenderTargetIdentifier cameraColorTarget = cameraColorHandle.nameID;

            if (!_sourceRT.RtId.Equals(cameraColorTarget) && _sourceRT.NameId != INVALID_NAME_ID && cmd != null)
            {
                cmd.ReleaseTemporaryRT(_sourceRT.NameId);
            }
        }

        protected override void Dispose(bool disposing)
        {
            _registerPass = null;
            _blurOptimizePass?.Dispose();
            _blurOptimizePass = null;
            _radialBlurPass?.Dispose();
            _radialBlurPass = null;
            _chromaticAberrationPass?.Dispose();
            _chromaticAberrationPass = null;
            _tiltShiftPass?.Dispose();
            _tiltShiftPass = null;
            _globalFogPass?.Dispose();
            _globalFogPass = null;
            _depthOnlyPass = null;
            _depthReleasePass = null;
            _vortexPass?.Dispose();
            _vortexPass = null;
            _lensDistortionPass?.Dispose();
            _lensDistortionPass = null;
            _hatchingPass?.Dispose();
            _hatchingPass = null;
            _fluctuationPass?.Dispose();
            _fluctuationPass = null;
            _colorCorrectionPass?.Dispose();
            _colorCorrectionPass = null;
            _colorEarlyResolvePass = null;
            _lateResolvePass = null;
            _exposurePass?.Dispose();
            _toneCurvePass?.Dispose();
            _exposurePass = _toneCurvePass = null;

            _sunShaftsPass?.Dispose();
            _indirectLightShaftsPass?.Dispose();
            _transmittedLightPass?.Dispose();
            _sunShaftsPass = null;
            _indirectLightShaftsPass = null;
            _transmittedLightPass = null;
            _lensFlarePass = null;
            _multiCameraCompositePass = null;
            _multiCameraFinalCompositePass = null;
            if (_dofDiffusionBloomOverlayPass != null)
            {
                _dofDiffusionBloomOverlayPass.Dispose();
                _dofDiffusionBloomOverlayPass = null;
            }

            _sourceSetupPass = null;
            _finalBlitPass = null;

            base.Dispose(disposing);
        }

        /// <summary>
        /// CameraData 原生偏移 +0x150 对应的 Parameter 解析。
        /// 字段真实名称确定后，可以直接替换为明确字段访问。
        /// </summary>
        private static Parameter ResolveFeatureParameter(CameraData cameraData)
        {
            if (cameraData == null)
                return null;

            object instance = cameraData;
            Type type = instance.GetType();
            const BindingFlags flags =
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic;

            while (type != null)
            {
                FieldInfo[] fields = type.GetFields(flags);
                for (int i = 0; i < fields.Length; i++)
                {
                    if (typeof(Parameter)
                        .IsAssignableFrom(fields[i].FieldType))
                    {
                        return fields[i].GetValue(instance)
                            as Parameter;
                    }
                }

                PropertyInfo[] properties = type.GetProperties(flags);

                for (int i = 0; i < properties.Length; i++)
                {
                    PropertyInfo property = properties[i];
                    if (!property.CanRead || property.GetIndexParameters().Length != 0)
                    {
                        continue;
                    }

                    if (!typeof(Parameter)
                        .IsAssignableFrom(property.PropertyType))
                    {
                        continue;
                    }

                    try
                    {
                        return property.GetValue(instance, null)
                            as Parameter;
                    }
                    catch
                    {
                        // 继续搜索基类中的参数字段。
                    }
                }

                type = type.BaseType;
            }

            return null;
        }

        /// <summary>
        /// 在 Dof Pass 前，把 Feature.SourceRT 指向 Camera Color。
        /// </summary>
        private sealed class SourceSetupPass : ScriptableRenderPass
        {
            private readonly PostImageEffectFeature _owner;

            public SourceSetupPass( PostImageEffectFeature owner, RenderPassEvent passEvent)
            {
                _owner = owner;
                renderPassEvent = passEvent;
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (_owner == null)
                    return;

                CommandBuffer cmd =
                    CommandBufferPool.Get(SOURCE_SETUP_POOL_NAME);

                try
                {
                    _owner.ResetSourceRT(cmd, ref renderingData);

                    context.ExecuteCommandBuffer(cmd);
                    LiveRuntimeDiagnostics.RecordRenderExecution(renderingData.cameraData.camera,
                        "SourceSetup", renderPassEvent, default, _owner.SourceRT);
                }
                finally
                {
                    CommandBufferPool.Release(cmd);
                }
            }
        }

        /// <summary>
        /// Copies the presentation camera's final colour into Director.MonitorTextureArray[i] for every monitor camera
        /// that is not rendering this frame.
        /// </summary>
        private sealed class MonitorScreenCapturePass : ScriptableRenderPass
        {
            private Director _director;

            public MonitorScreenCapturePass(RenderPassEvent passEvent)
            {
                renderPassEvent = passEvent;
            }

            public bool Setup(Director director)
            {
                _director = director;
                RenderTexture[] textures = director != null ? director.MonitorTextureArray : null;
                if (textures == null || textures.Length == 0)
                    return false;
                for (int i = 0; i < textures.Length; i++)
                    if (textures[i] != null && !director.IsActiveMonitorCamera(i))
                        return true;
                return false;
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                RenderTexture[] textures = _director != null ? _director.MonitorTextureArray : null;
                if (textures == null)
                    return;
                RTHandle color = renderingData.cameraData.renderer.cameraColorTargetHandle;
                if (color == null)
                    return;

                CommandBuffer cmd = CommandBufferPool.Get("PostImageEffectFeature.MonitorScreenCapture");
                try
                {
                    for (int i = 0; i < textures.Length; i++)
                    {
                        if (textures[i] == null || _director.IsActiveMonitorCamera(i))
                            continue;
                        cmd.Blit(color.nameID, textures[i]);
                    }
                    context.ExecuteCommandBuffer(cmd);
                }
                finally
                {
                    CommandBufferPool.Release(cmd);
                }
            }
        }

        /// <summary>
        /// Dof Pass 在 550 或更早执行时，只更新 Feature.SourceRT。
        /// 此 Pass 将结果写回 Camera Color，并释放 Dof 临时 RT。
        /// </summary>
        private sealed class FinalBlitPass : ScriptableRenderPass
        {
            private readonly PostImageEffectFeature _owner;
            private readonly string _traceStep;

            public FinalBlitPass(PostImageEffectFeature owner, RenderPassEvent passEvent, string traceStep = "FinalBlit")
            {
                _owner = owner;
                _traceStep = traceStep;
                renderPassEvent = passEvent;
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (_owner == null)
                    return;

                RenderTextureHandle source = _owner.SourceRT;
                if (source.NameId == INVALID_NAME_ID)
                    return;

                ScriptableRenderer renderer = renderingData.cameraData.renderer;

                if (renderer == null)
                    return;

                RTHandle cameraColorHandle = renderer.cameraColorTargetHandle;
                if (cameraColorHandle == null)
                    return;

                RenderTargetIdentifier cameraColorTarget = cameraColorHandle.nameID;

                CommandBuffer cmd =
                    CommandBufferPool.Get(FINAL_BLIT_POOL_NAME);

                try
                {
                    if (!source.RtId.Equals(cameraColorTarget))
                    {
                        cmd.Blit(
                            source.RtId,
                            cameraColorTarget);
                    }

                    // Blit 后释放 Dof 输出临时 RT,并把 SourceRT 恢复为 Camera Color
                    _owner.ResetSourceRT(cmd, ref renderingData);

                    context.ExecuteCommandBuffer(cmd);
                    LiveRuntimeDiagnostics.RecordRenderExecution(renderingData.cameraData.camera,
                        _traceStep, renderPassEvent, source, _owner.SourceRT);
                }
                finally
                {
                    CommandBufferPool.Release(cmd);
                }
            }
        }

        public class Parameter
        {
            private Camera _targetCamera;
            private CameraData _targetCameraData;

            public bool IsEnable;

            // Native Parameter.BlurOptimized (+0x24, right after IsEnable); ctor = BlurOptimizePass.Parameter.Default().
            public BlurOptimizePass.Parameter BlurOptimized = BlurOptimizePass.Parameter.Default();

            // DofDiffusionBloomOverlayPass 实际使用的参数。
            public DofDiffusionBloomOverlayPass.Parameter
                DofDiffuionBloomOverlay;

            public RadialBlurPass.Parameter RadialBlur = RadialBlurPass.Parameter.Default();
            public ChromaticAberrationPass.Parameter ChromaticAberration = default;
            public TiltShiftPass.Parameter TiltShift;
            public GlobalFogPass.Parameter GlobalFog;
            public VortexPass.Parameter Vortex;
            public LensDistortionPass.Parameter LensDistortion;
            public HatchingPass.Parameter Hatching;
            public FluctuationPass.Parameter Fluctuation;
            public SimpleColorCorrectionPass.Parameter ColorCorrection;
            public SunShaftsPass.Parameter SunShafts = SunShaftsPass.Parameter.Default();
            public IndirectLightShaftsPass.Parameter LightShafts;

            // Native ToneCurve/Exposure parameter blocks consumed at event550.
            public Gallop.ImageEffect.ExposureParam ExposureParam =
                new Gallop.ImageEffect.ExposureParam();
            public Gallop.ImageEffect.ToneCurveParam ToneCurveParam =
                new Gallop.ImageEffect.ToneCurveParam();

            public bool IsRequestDepthPass;

            public Camera TargetCamera
            {
                get { return _targetCamera; }
                set
                {
                    _targetCamera = value;
                    DofDiffuionBloomOverlay.TargetCamera = value;

                    if (_targetCamera == null)
                        return;

                    if (!_targetCamera.TryGetComponent(
                            out _targetCameraData))
                    {
                        _targetCameraData = _targetCamera.gameObject.AddComponent<CameraData>();

                        InvokeCameraDataInitialize( _targetCameraData);
                    }
                }
            }

            public bool IsUseDepthTexture
            {
                get
                {
                    // The recovered IsDepthTexture property represents the original
                    // overlay/depth-request contract, not URP's complete DOF input needs.
                    var mode = DofDiffuionBloomOverlay.UseDofDiffusionBloomType;
                    return (RadialBlur.IsValid && RadialBlur.IsEnabledDepth) ||
                           mode == DofDiffusionBloomOverlayParam.DofDiffusionBloomType.DofBloom ||
                           mode == DofDiffusionBloomOverlayParam.DofDiffusionBloomType.DiffusionDofBloom ||
                           mode == DofDiffusionBloomOverlayParam.DofDiffusionBloomType.Dof ||
                           mode == DofDiffusionBloomOverlayParam.DofDiffusionBloomType.OldDof ||
                           mode == DofDiffusionBloomOverlayParam.DofDiffusionBloomType.OldDofFastBloom ||
                           DofDiffuionBloomOverlay.IsDepthTexture;
                }
            }

            public Parameter()
            {
                IsEnable = true;
                DofDiffuionBloomOverlay = DofDiffusionBloomOverlayPass.Parameter.Default();
                IsRequestDepthPass = true;
            }

            public void ShallowCopyFrom(Parameter src)
            {
                if (src == null)
                    throw new ArgumentNullException(nameof(src));

                ShallowCopyParameters(src);
                IsRequestDepthPass = src.IsRequestDepthPass;
            }

            public void ShallowCopyParameters(Parameter src)
            {
                if (src == null)
                    throw new ArgumentNullException(nameof(src));

                // Native ShallowCopyParameters (0x7ffd595c3ba0) copies +0x24..+0x3C first.
                BlurOptimized = src.BlurOptimized;
                DofDiffuionBloomOverlay =
                    src.DofDiffuionBloomOverlay;
                RadialBlur = src.RadialBlur;
                ChromaticAberration = src.ChromaticAberration;
                TiltShift = src.TiltShift;
                GlobalFog = src.GlobalFog;
                Vortex = src.Vortex;
                LensDistortion = src.LensDistortion;
                Hatching = src.Hatching;
                Fluctuation = src.Fluctuation;
                ColorCorrection = src.ColorCorrection;
                SunShafts = src.SunShafts;
                LightShafts = src.LightShafts;
            }

            public void CopyFromGallopImageEffect(
                GallopImageEffectParameter imageEffectParam)
            {
                if (imageEffectParam == null)
                {
                    throw new ArgumentNullException( nameof(imageEffectParam));
                }

                DofDiffuionBloomOverlay.Setup(imageEffectParam.DofDiffusionBloomOverlayParam);
            }

            private static void InvokeCameraDataInitialize(
                CameraData cameraData)
            {
                if (cameraData == null)
                    return;

                MethodInfo[] methods =
                    cameraData.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                for (int i = 0; i < methods.Length; i++)
                {
                    MethodInfo method = methods[i];
                    if (method.Name != "Initialize")
                        continue;

                    ParameterInfo[] parameters = method.GetParameters();

                    if (parameters.Length != 1)
                        continue;

                    Type parameterType = parameters[0].ParameterType;

                    if (parameterType == typeof(bool))
                    {
                        method.Invoke(cameraData, new object[] { false });
                        return;
                    }

                    if (parameterType == typeof(int))
                    {
                        method.Invoke(cameraData, new object[] { 0 });
                        return;
                    }
                }
            }
        }
    }
}

