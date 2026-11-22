using Gallop.Live.Cutt;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static Gallop.DrivenKeyComponent;
using static Gallop.Live.Cutt.LiveTimelineDefine;

namespace Gallop
{
    public class FaceDrivenKeyTarget : ScriptableObject
    {
        public UmaContainerCharacter Container;

        public List<EyeTarget> _eyeTarget;
        public List<EyebrowTarget> _eyebrowTarget;
        public List<MouthTarget> _mouthTarget;
        public List<TargetInfomation> _earTarget;

        public Dictionary<string, Transform> Objs;
        private FacialMorph BaseLEarMorph, BaseREarMorph;
        private FacialMorph BaseLEyeBrowMorph, BaseREyeBrowMorph;
        private FacialMorph BaseLEyeMorph, BaseREyeMorph;
        private FacialMorph BaseMouthMorph;
        public FacialMorph LeftEyeXrange, LeftEyeYrange, RightEyeXrange, RightEyeYrange;


        public List<FacialMorph> EarMorphs = new List<FacialMorph>();
        public List<FacialMorph> EyeBrowMorphs = new List<FacialMorph>();
        public List<FacialMorph> EyeMorphs = new List<FacialMorph>();
        public List<FacialMorph> MouthMorphs = new List<FacialMorph>();
        public List<FacialOtherMorph> OtherMorphs = new List<FacialOtherMorph>();
        public List<FacialMorph> AllMorphs => EarMorphs.Concat(EyeBrowMorphs).Concat(EyeMorphs).Concat(MouthMorphs).Concat(OtherMorphs).ToList();

        public Transform DrivenKeyLocator;
        public Transform EyeballCtrl_L_Locator, EyeballCtrl_R_Locator, EyeballCtrl_All_Locator;

        Dictionary<Transform, Vector3> RotationRecorder = new Dictionary<Transform, Vector3>();

        List<FacialMorph> leftEyeMorph = new List<FacialMorph>();
        List<FacialMorph> rightEyeMorph = new List<FacialMorph>();

        List<FacialMorph> leftEyebrowMorph = new List<FacialMorph>();
        List<FacialMorph> rightEyebrowMorph = new List<FacialMorph>();

        List<FacialMorph> leftEarMorph = new List<FacialMorph>();
        List<FacialMorph> rightEarMorph = new List<FacialMorph>();

        FacialOtherMorph CheekMorph;
        FacialOtherMorph ShadeMorph;
        FacialOtherMorph MangaMorph;
        FacialOtherMorph StaticTearMorph;
        List<FacialOtherMorph> TearMorphs = new List<FacialOtherMorph>();

        // Gallop.Live.LiveFaceController (ear path) + Gallop.EarWeightGroup (DrivenKeyComponent ear weights), one
        // pair per character, created on first use by the live facial update.
        private EarWeightGroup _earWeightGroup;
        private Gallop.Live.LiveFaceController _liveFaceController;
        private float _earPrevLiveTime = -1f;

        // Ear random motion (timeline keys with useEarRandomMotion): the LiveModelController ear members of this
        // character (see LiveModelControllerEarMotion) and the DrivenKeyComponent that plays the ear DrivenKeyAnimation
        // on the driven-key locator.
        private Gallop.Live.LiveModelControllerEarMotion _liveEarMotion;
        private DrivenKeyComponent _earDrivenKey;

        private void EnsureLiveFaceController()
        {
            if (_liveFaceController == null)
            {
                _earWeightGroup = new EarWeightGroup();
                _liveFaceController = new Gallop.Live.LiveFaceController(_earWeightGroup, new FacialPartsApplier(this));
            }
        }

        public Gallop.Live.LiveModelControllerEarMotion GetLiveEarMotion()
        {
            return _liveEarMotion ?? (_liveEarMotion = new Gallop.Live.LiveModelControllerEarMotion(this));
        }

        // Builds the DrivenKeyComponent ear state on first use (the game's DrivenKeyComponent of the head): the ear
        // weight group, the driven-key locator Animator and the per-EarType nodes of Ear_L_Ctrl (side 1) /
        // Ear_R_Ctrl (side 0), the same side numbering as LiveFaceController.SetEar.
        private bool EnsureEarDrivenKey()
        {
            if (_earDrivenKey != null) return true;
            if (!DrivenKeyLocator) return false;
            var animator = DrivenKeyLocator.GetComponent<Animator>();
            var left = DrivenKeyLocator.Find("Ear_L_Ctrl");
            var right = DrivenKeyLocator.Find("Ear_R_Ctrl");
            if (animator == null || left == null || right == null) return false;
            EnsureLiveFaceController();
            var component = new DrivenKeyComponent();
            component.InitializeEar(_earWeightGroup, animator);
            component.InitializeEarDrivenKeyInfo(1, left);
            component.InitializeEarDrivenKeyInfo(0, right);
            _earDrivenKey = component;
            return true;
        }

        // ModelController.PlayEarDrivenKey @0x7ffd596783e0 -> DrivenKeyComponent.PlayEar(animation, 0, 0, 0)
        // @0x7ffd58e6fa20 (its tail enables the DrivenKeyPlayableAnimator's Animator).
        public void PlayEarDrivenKey(DrivenKeyAnimation animation)
        {
            if (animation == null || !EnsureEarDrivenKey()) return;
            DrivenKeyLocator.GetComponent<Animator>().enabled = true;
            _earDrivenKey.PlayEar(animation);
        }

        // DrivenKeyComponent.StopLayer(1) (LiveModelController.StopEarRandomMotion @0x7ffd59b3b980).
        public void StopEarDrivenKeyLayer()
        {
            _earDrivenKey?.StopEarLayer();
        }

        public void Initialize(Dictionary<string, Transform> objs)
        {
            Objs = objs;

            for (int i = 0; i < _earTarget.Count; i++)
            {
                for (int j = 0; j < _earTarget[i]._faceGroupInfo.Count; j++)
                {
                    FacialMorph morph = new FacialMorph();
                    morph.index = i;
                    morph.direction = j > 0;
                    morph.name = "Ear_" + i + "_" + (morph.direction ? "L" : "R");
                    morph.tag = Enum.GetName(typeof(EarType), i);
                    morph.trsArray = _earTarget[i]._faceGroupInfo[j]._trsArray;
                    foreach (TrsArray trs in morph.trsArray)
                    {
                        Objs.TryGetValue(trs._path,out trs.transform);
                    }
                    if (i == 0)
                    {
                        if (morph.direction) BaseLEarMorph = morph;
                        else BaseREarMorph = morph;
                    }
                    else
                    {
                        if (morph.direction) leftEarMorph.Add(morph);
                        else rightEarMorph.Add(morph);

                        EarMorphs.Add(morph);
                    }
                }
            }

            for (int i = 0; i < _eyebrowTarget.Count; i++)
            {
                for (int j = 0; j < _eyebrowTarget[i]._faceGroupInfo.Count; j++)
                {
                    FacialMorph morph = new FacialMorph();
                    morph.index = i;
                    morph.direction = j > 0;
                    morph.name = "EyeBrow_" + i + "_" + (morph.direction ? "L" : "R");
                    morph.tag = Enum.GetName(typeof(LiveTimelineDefine.FacialEyebrowId), i);
                    morph.trsArray = _eyebrowTarget[i]._faceGroupInfo[j]._trsArray;
                    foreach (TrsArray trs in morph.trsArray)
                    {
                        Objs.TryGetValue(trs._path, out trs.transform);
                    }
                    if (i == 0)
                    {
                        if (morph.direction) BaseLEyeBrowMorph = morph;
                        else BaseREyeBrowMorph = morph;
                    }
                    else
                    {
                        if (morph.direction) leftEyebrowMorph.Add(morph);
                        else rightEyebrowMorph.Add(morph);

                        EyeBrowMorphs.Add(morph);
                    }
                }
            }

            for (int i = 0; i < _eyeTarget.Count; i++)
            {
                for (int j = 0; j < _eyeTarget[i]._faceGroupInfo.Count; j++)
                {
                    FacialMorph morph = new FacialMorph();
                    morph.index = i;
                    morph.direction = j > 0;
                    morph.name = "Eye_" + i + "_" + (morph.direction ? "L" : "R");
                    morph.tag = Enum.GetName(typeof(LiveTimelineDefine.FacialEyeId), i);
                    morph.trsArray = _eyeTarget[i]._faceGroupInfo[j]._trsArray;
                    foreach (TrsArray trs in morph.trsArray)
                    {
                         Objs.TryGetValue(trs._path,out trs.transform);
                    }
                    if (i == 0)
                    {
                        if (morph.direction) BaseLEyeMorph = morph;
                        else BaseREyeMorph = morph;
                    }
                    else
                    {
                        if (i == 20)
                        {
                            if (morph.direction) LeftEyeXrange = morph;
                            else RightEyeXrange = morph;
                        }
                        else if (i == 21)
                        {
                            if (morph.direction) LeftEyeYrange = morph;
                            else RightEyeYrange = morph;
                        }

                        if (morph.direction) leftEyeMorph.Add(morph);
                        else rightEyeMorph.Add(morph);

                        EyeMorphs.Add(morph);
                    }
                }
            }

            for (int i = 0; i < _mouthTarget.Count; i++)
            {
                for (int j = 0; j < _mouthTarget[i]._faceGroupInfo.Count; j++)
                {
                    FacialMorph morph = new FacialMorph();
                    morph.index = i;
                    morph.direction = j > 0;
                    morph.name = "Mouth_" + i + "_" + j;
                    morph.tag = Enum.GetName(typeof(LiveTimelineDefine.FacialMouthId), i);
                    morph.trsArray = _mouthTarget[i]._faceGroupInfo[j]._trsArray;
                    foreach (TrsArray trs in morph.trsArray)
                    {
                        Objs.TryGetValue(trs._path, out trs.transform);
                    }
                    if (i == 0)
                    {
                        BaseMouthMorph = morph;
                    }
                    else
                    {
                        MouthMorphs.Add(morph);
                    }
                }
            }

            InitializeExtra();
            SetupAnimator();

            if (UmaViewerUI.Instance)
                UmaViewerUI.Instance.LoadFacialPanels(this);
        }

        public void InitializeExtra()
        {
            if (Container.CheekTex_0)
            {
                var CheekMorph = new FacialOtherMorph
                {
                    name = "Cheek_Ctrl",
                    locator = DrivenKeyLocator.Find("Cheek_Ctrl"),
                    BindGameObject = Container.Head.transform.Find("M_Cheek").gameObject
                };
                var bindMaterial = CheekMorph.BindGameObject.GetComponent<SkinnedMeshRenderer>().material;

                BindProperty cheekProperty = new BindProperty()
                {
                    BindMaterial = bindMaterial,
                    Part = BindProperty.LocatorPart.ScaX,
                    Type = BindProperty.BindType.Texture,
                    BindTexture = Container.CheekTex_0,
                    PropertyName = "_CheekRate"
                };
                CheekMorph.BindProperties.Add(cheekProperty);

                if (Container.CheekTex_1)
                {
                    BindProperty cheekProperty1 = new BindProperty()
                    {
                        BindMaterial = bindMaterial,
                        Part = BindProperty.LocatorPart.ScaY,
                        Type = BindProperty.BindType.Texture,
                        BindTexture = Container.CheekTex_1,
                        PropertyName = "_CheekRate"
                    };
                    CheekMorph.BindProperties.Add(cheekProperty1);
                }

                if (Container.CheekTex_2)
                {
                    BindProperty cheekProperty2 = new BindProperty()
                    {
                        BindMaterial = bindMaterial,
                        Part = BindProperty.LocatorPart.ScaZ,
                        Type = BindProperty.BindType.Texture,
                        BindTexture = Container.CheekTex_2,
                        PropertyName = "_CheekRate"
                    };
                    CheekMorph.BindProperties.Add(cheekProperty2);
                }
                OtherMorphs.Add(CheekMorph);
                this.CheekMorph = CheekMorph;
            }

            Container.FaceMaterial = Container.Head.transform.Find("M_Face").GetComponent<SkinnedMeshRenderer>().material;
            if (Container.FaceMaterial)
            {
                FacialOtherMorph FaceShadowMorph = new FacialOtherMorph()
                {
                    name = "Shade_Ctrl",
                    locator = DrivenKeyLocator.Find("Shade_Ctrl")
                };
                FaceShadowMorph.BindProperties.Add(
                    new BindProperty()
                    {
                        BindMaterial = Container.FaceMaterial,
                        Part = BindProperty.LocatorPart.ScaX,
                        Type = BindProperty.BindType.Shader,
                        PropertyName = "_faceShadowAlpha"
                    });
                OtherMorphs.Add(FaceShadowMorph);
                ShadeMorph = FaceShadowMorph;
            }

            FacialOtherMorph FaceMangaMorph = new FacialOtherMorph()
            {
                name = "Manga_Ctrl",
                locator = DrivenKeyLocator.Find("Manga_Ctrl")
            };

            var mangaObjects = new List<GameObject>();
            mangaObjects.AddRange(Container.LeftMangaObject);
            mangaObjects.AddRange(Container.RightMangaObject);
            FaceMangaMorph.BindProperties.Add(new BindProperty()
            {
                Part = BindProperty.LocatorPart.ScaY,
                Type = BindProperty.BindType.EyeSelect,
                BindPrefabs = mangaObjects
            });
            FaceMangaMorph.BindProperties.Add(new BindProperty()
            {
                Part = BindProperty.LocatorPart.ScaX,
                Type = BindProperty.BindType.Enable,
                BindPrefabs = mangaObjects
            });
            OtherMorphs.Add(FaceMangaMorph);
            MangaMorph = FaceMangaMorph;

            if(Container.StaticTear_L && Container.StaticTear_R)
            {
                FacialOtherMorph StaticTearMorph = new FacialOtherMorph()
                {
                    name = "StaticTear_Ctrl",
                    locator = DrivenKeyLocator.Find("Cheek_Ctrl")
                };

                StaticTearMorph.BindProperties.Add(new BindProperty()
                {
                    Part = BindProperty.LocatorPart.PosX,
                    Type = BindProperty.BindType.Enable,
                    BindPrefabs = new List<GameObject>() { Container.StaticTear_L }
                });

                StaticTearMorph.BindProperties.Add(new BindProperty()
                {
                    Part = BindProperty.LocatorPart.PosY,
                    Type = BindProperty.BindType.Enable,
                    BindPrefabs = new List<GameObject>() { Container.StaticTear_R }
                });

                this.StaticTearMorph = StaticTearMorph;
                OtherMorphs.Add(StaticTearMorph);
            }

            foreach (var tear in Container.TearControllers)
            {
                var index = Container.TearControllers.IndexOf(tear);
                FacialOtherMorph FaceTearMorph = new FacialOtherMorph()
                {
                    name = $"Tear{index}_Ctrl",
                    locator = DrivenKeyLocator.Find($"Tear{index}_Ctrl")
                };
                FaceTearMorph.BindProperties.Add(new BindProperty()
                {
                    Part = BindProperty.LocatorPart.ScaX,
                    Type = BindProperty.BindType.TearWeight,
                    BindTearController = tear,
                    Value = tear.Weight,
                    DefaultValue = tear.Weight
                });
                FaceTearMorph.BindProperties.Add(new BindProperty()
                {
                    Part = BindProperty.LocatorPart.PosY,
                    Type = BindProperty.BindType.TearSide,
                    BindTearController = tear,
                    Value = tear.CurrentDir,
                    DefaultValue = tear.CurrentDir
                });
                FaceTearMorph.BindProperties.Add(new BindProperty()
                {
                    Part = BindProperty.LocatorPart.ScaY,
                    Type = BindProperty.BindType.TearSelect,
                    BindTearController = tear,
                    Value = tear.CurrenObject,
                    DefaultValue = tear.CurrenObject
                });
                FaceTearMorph.BindProperties.Add(new BindProperty()
                {
                    Part = BindProperty.LocatorPart.ScaZ,
                    Type = BindProperty.BindType.TearSpeed,
                    BindTearController = tear,
                    Value = tear.Speed,
                    DefaultValue = tear.Speed
                });
                OtherMorphs.Add(FaceTearMorph);
                TearMorphs.Add(FaceTearMorph);
            }
        }

        public void SetupAnimator()
        {
            if (!DrivenKeyLocator) return;

            SetupLocator("Ear_L_Ctrl", EarMorphs.FindAll(a => a.direction == true));
            SetupLocator("Ear_R_Ctrl", EarMorphs.FindAll(a => a.direction == false));
            SetupLocator("Eye_L_Base_Ctrl", EyeMorphs.FindAll(a => a.direction == true));
            SetupLocator("Eye_R_Base_Ctrl", EyeMorphs.FindAll(a => a.direction == false));
            SetupLocator("Eyebrow_L_Base_Ctrl", EyeBrowMorphs.FindAll(a => a.direction == true));
            SetupLocator("Eyebrow_R_Base_Ctrl", EyeBrowMorphs.FindAll(a => a.direction == false));
            SetupLocator("Mouth_Base_Ctrl", MouthMorphs.FindAll(a => a.direction == false));

            EyeballCtrl_L_Locator = DrivenKeyLocator.transform.Find("Eyeball_L_Ctrl");
            EyeballCtrl_R_Locator = DrivenKeyLocator.transform.Find("Eyeball_R_Ctrl");
            EyeballCtrl_All_Locator = DrivenKeyLocator.transform.Find("Eyeball_all_Ctrl");

            Container.UmaFaceAnimator = DrivenKeyLocator.GetComponent<Animator>();
            Container.UmaFaceAnimator.avatar = AvatarBuilder.BuildGenericAvatar(DrivenKeyLocator.gameObject, "DrivenKeyLocator");
            Container.FaceOverrideController = Instantiate(UmaViewerBuilder.Instance.FaceOverrideController);
            Container.UmaFaceAnimator.runtimeAnimatorController = Container.FaceOverrideController;
        }

        public void SetupLocator(string rootName, List<FacialMorph> morphs)
        {

            var root = DrivenKeyLocator.transform.Find(rootName);
            var locators = new List<Transform>(root.GetComponentsInChildren<Transform>());
            morphs.ForEach(morph =>
            {
                var locator = locators.Find(a => a.name.EndsWith(morph.tag));
                if (locator)
                {
                    morph.locator = locator;
                }
            });
        }

        // Gallop.Model.Component.EyeHighlightController of this head (set by UmaContainerCharacter.LoadFaceMorph).
        // The game runs its BeforeApplyDrivenKey / AfterApplyDrivenKey around the driven-key application; here that
        // is the eye morph pass (reset -> additive morphs), whose result moves the Eye_*_info highlight locators.
        [System.NonSerialized] public Gallop.Model.Component.EyeHighlightController EyeHighlight;
        // LiveFaceController +0x90 (last tearyType of the effect key).
        [System.NonSerialized] private int _prevTearyType;

        public void ChangeMorph()
        {
            FacialResetAll();
            EyeHighlight?.BeforeApplyDrivenKey();
            if (Container.FaceOverrideData && Container.FaceOverrideData.Enable)
            {
                OverrideFace(Container.FaceOverrideData);
            }
            EarMorphs.ForEach(morph => ProcessMorph(morph));
            EyeBrowMorphs.ForEach(morph => ProcessMorph(morph));
            EyeMorphs.ForEach(morph => ProcessMorph(morph));
            MouthMorphs.ForEach(morph => ProcessMorph(morph));
            OtherMorphs.ForEach(morph => ProcessExtraMorph(morph));
            ApplyRotation();
            EyeHighlight?.AfterApplyDrivenKey();
        }

        public void ChangeMorphEye()
        {
            FacialResetEye();
            EyeHighlight?.BeforeApplyDrivenKey();
            if (Container.FaceOverrideData && Container.FaceOverrideData.Enable)
            {
                OverrideFace(Container.FaceOverrideData);
            }
            EyeMorphs.ForEach(morph => ProcessMorph(morph));
            ApplyRotationEye();
            EyeHighlight?.AfterApplyDrivenKey();
        }

        public void ChangeMorphMouth()
        {
            FacialResetMouth();
            MouthMorphs.ForEach(morph => ProcessMorph(morph));
            ApplyRotationMouth();
        }

        public void ChangeMorphEyebrow()
        {
            FacialResetEyebrow();
            EyeBrowMorphs.ForEach(morph => ProcessMorph(morph));
            ApplyRotationEyebrow();
        }

        public void ChangeMorphEar()
        {
            FacialResetEar();
            EarMorphs.ForEach(morph => ProcessMorph(morph));
            ApplyRotationEar();
        }

        // EarWeightGroup weights -> ear bone TRS: the game's EarWeightJob (SetTRS / CalcTRS / AddTRS / BlendTRS, RVA
        // 0x4AF78B0 / 0x4AF75B0) ported in EarWeightJob.cs and run by EarWeightJobRunner (see its header). The ear bones
        // are written by the job, not by the viewer's additive morph pass (ChangeMorphEar stays for the viewer UI only).
        private EarWeightJobRunner _earJobRunner;

        private void ApplyEarWeights()
        {
            if (_earJobRunner == null)
            {
                _earJobRunner = new EarWeightJobRunner(_earTarget);
                RegisterEarVirtualParents();
            }
            _earJobRunner.Apply(_earWeightGroup);
        }

        // ModelController.InitDrivenKey @0x7ffd59674700: DrivenKeyComponent.SetVirtualParentTransform (0x4AF27F0) for the
        // second and third ear bone of each side, following the CySpring ear chain. Group 1 = left, 0 = right (as
        // LiveFaceController.SetEar). The names are the values the game passes (read from the game at those calls):
        //   group 1: Ear_02_L (root Sp_He_Ear0_L_00, parent Sp_He_Ear0_L_01), Ear_03_L (root _01, parent _02)
        //   group 0: Ear_02_R (root Sp_He_Ear0_R_00, parent Sp_He_Ear0_R_01), Ear_03_R (root _01, parent _02)
        private void RegisterEarVirtualParents()
        {
            if (_earJobRunner == null || !_earJobRunner.IsValid || Container == null) return;
            var all = Container.transform.GetComponentsInChildren<Transform>(true);
            Transform Find(string name)
            {
                foreach (var t in all) if (t != null && t.name == name) return t;
                return null;
            }

            var entries = new[]
            {
                (group: 1, target: "Ear_02_L", root: "Sp_He_Ear0_L_00", parent: "Sp_He_Ear0_L_01"),
                (group: 1, target: "Ear_03_L", root: "Sp_He_Ear0_L_01", parent: "Sp_He_Ear0_L_02"),
                (group: 0, target: "Ear_02_R", root: "Sp_He_Ear0_R_00", parent: "Sp_He_Ear0_R_01"),
                (group: 0, target: "Ear_03_R", root: "Sp_He_Ear0_R_01", parent: "Sp_He_Ear0_R_02"),
            };
            foreach (var e in entries)
                _earJobRunner.SetVirtualParentTransform(e.group, e.target, Find(e.root), Find(e.parent));
        }

        // DrivenKeyComponent.ApplyVirtualParentTransform (0x4AE9560); called by the CySpring step right after EndSimulation.
        public void ApplyVirtualParentTransform()
        {
            _earJobRunner?.ApplyVirtualParentTransform();
        }

        public void ChangeMorphEffect()
        {
            OtherMorphs.ForEach(morph => ProcessExtraMorph(morph));
        }

        public void ClearAllWeights()
        {
            EarMorphs.ForEach(morph => morph.weight = 0);
            EyeBrowMorphs.ForEach(morph => morph.weight = 0);
            EyeMorphs.ForEach(morph => morph.weight = 0);
            MouthMorphs.ForEach(morph => morph.weight = 0);

            OtherMorphs.ForEach(morph =>
            {
                morph.BindProperties.ForEach(property =>
                {
                    property.Value = property.DefaultValue;
                });
            });
        }

        private void ProcessMorph(FacialMorph morph)
        {
            var isOverride = Container.FaceOverrideData && Container.FaceOverrideData.Enable;
            if (isOverride && morph.OverrideType == OverrideType.Ignore) return;
            if (morph.weight == 0 && (!isOverride || morph.OverrideType == OverrideType.None))return;

            var weight = (isOverride && morph.OverrideType == OverrideType.Override ? morph.overrideWeight : morph.weight);

            foreach (TrsArray trs in morph.trsArray)
            {
                if (trs.transform)
                {
                    trs.transform.localScale += trs._scale * weight;
                    trs.transform.localPosition += trs._position * weight;
                    RotationRecorder[trs.transform] += trs._rotation * weight;
                }
            }
        }

        private void ProcessExtraMorph(FacialOtherMorph morph)
        {
            foreach (var property in morph.BindProperties)
            {
                switch (property.Type)
                {
                    case BindProperty.BindType.Texture:
                        if (property.Value > 0.01f)
                        {
                            if (!morph.BindGameObject.activeSelf) morph.BindGameObject.SetActive(true);
                            property.BindMaterial.mainTexture = property.BindTexture;
                            property.BindMaterial.SetFloat(property.PropertyName, property.Value);
                        }
                        else
                        {
                            if (morph.BindProperties.Find(p =>p.Value >0) == null)
                            {
                                if (morph.BindGameObject.activeSelf) morph.BindGameObject.SetActive(false);
                            }
                        }
                        break;
                    case BindProperty.BindType.Shader:
                        property.BindMaterial.SetFloat(property.PropertyName, property.Value);
                        break;
                    case BindProperty.BindType.Select:
                        // (RENDIMIENTO) SetActive solo si cambia: este bucle corre por morph y frame y
                        // antes activaba/desactivaba todos los prefabs aunque ya estuvieran en su estado.
                        for (int i = 0; i < property.BindPrefabs.Count; i++)
                        {
                            var bindGo = property.BindPrefabs[i];
                            bool want = (int)property.Value == i;
                            if (bindGo != null && bindGo.activeSelf != want) bindGo.SetActive(want);
                        }
                        break;
                    case BindProperty.BindType.EyeSelect:
                        var count = property.BindPrefabs.Count / 2;
                        var val = (int)property.Value;
                        for (int i = 0; i < property.BindPrefabs.Count; i++)
                        {
                            var bindGo = property.BindPrefabs[i];
                            bool want = val + count < property.BindPrefabs.Count && (val == i || val + count == i);
                            if (bindGo != null && bindGo.activeSelf != want) bindGo.SetActive(want);
                        }
                        break;
                    case BindProperty.BindType.Enable:
                        if(property.BindPrefabs.Count == 1)
                        {
                            var bindGo = property.BindPrefabs[0];
                            bool want = property.Value > 0;
                            if (bindGo != null && bindGo.activeSelf != want) bindGo.SetActive(want);
                        }
                        else if(property.Value <= 0)
                        {
                            for (int i = 0; i < property.BindPrefabs.Count; i++)
                            {
                                var bindGo = property.BindPrefabs[i];
                                if (bindGo != null && bindGo.activeSelf) bindGo.SetActive(false);
                            }
                        }
                        break;
                    case BindProperty.BindType.TearSide:
                        property.BindTearController.SetDir((int)property.Value);
                        break;
                    case BindProperty.BindType.TearWeight:
                        property.BindTearController.SetWegiht(property.Value);
                        break;
                    case BindProperty.BindType.TearSelect:
                        property.BindTearController.SetObject((int)property.Value);
                        break;
                    case BindProperty.BindType.TearSpeed:
                        property.BindTearController.SetSpeed(property.Value);
                        break;
                }
            }
        }

        public void FacialResetAll()
        {
            FacialReset(BaseLEarMorph.trsArray);
            FacialReset(BaseREarMorph.trsArray);
            FacialReset(BaseLEyeBrowMorph.trsArray);
            FacialReset(BaseREyeBrowMorph.trsArray);
            FacialReset(BaseLEyeMorph.trsArray);
            FacialReset(BaseREyeMorph.trsArray);
            FacialReset(BaseMouthMorph.trsArray);
            ApplyRotation();
        }

        public void FacialResetEye()
        {
            FacialReset(BaseLEyeMorph.trsArray);
            FacialReset(BaseREyeMorph.trsArray);
        }

        public void FacialResetMouth()
        {
            FacialReset(BaseMouthMorph.trsArray);
        }

        public void FacialResetEyebrow()
        {
            FacialReset(BaseLEyeBrowMorph.trsArray);
            FacialReset(BaseREyeBrowMorph.trsArray);
        }

        public void FacialResetEar()
        {
            FacialReset(BaseLEarMorph.trsArray);
            FacialReset(BaseREarMorph.trsArray);
        }

        public void FacialResetEffect()
        {
            foreach(var morphs in OtherMorphs)
            {
                foreach(var property in morphs.BindProperties)
                {
                    property.Value = property.DefaultValue;
                }
            }
        }

        public void FacialReset(List<TrsArray> trsArrays)
        {
            foreach (TrsArray trs in trsArrays)
            {
                if (trs.transform)
                {
                    trs.transform.localPosition = trs._position;
                    trs.transform.localScale = trs._scale;
                    RotationRecorder[trs.transform] = trs._rotation;
                }
            }
        }

        public void ApplyRotation()
        {
            foreach (var trs in RotationRecorder)
            {
                trs.Key.transform.localRotation = Gallop.Math.FromMayaEuler(trs.Value);
            }
        }

        public void ApplyRotationEye()
        {
            Action<List<TrsArray>> applyRotation = delegate (List<TrsArray> trsArray)
            {
               foreach (TrsArray trs in trsArray)
               {
                   if(trs.transform)
                   trs.transform.localRotation = Gallop.Math.FromMayaEuler(RotationRecorder[trs.transform]);
               }
            };
            applyRotation.Invoke(BaseLEyeMorph.trsArray);
            applyRotation.Invoke(BaseREyeMorph.trsArray);
        }

        public void ApplyRotationMouth()
        {
            Action<List<TrsArray>> applyRotation = delegate (List<TrsArray> trsArray)
            {
                foreach (TrsArray trs in trsArray)
                {
                    if (trs.transform)
                        trs.transform.localRotation = Gallop.Math.FromMayaEuler(RotationRecorder[trs.transform]);
                }
            };
            applyRotation.Invoke(BaseMouthMorph.trsArray);
        }

        public void ApplyRotationEyebrow()
        {
            Action<List<TrsArray>> applyRotation = delegate (List<TrsArray> trsArray)
            {
                foreach (TrsArray trs in trsArray)
                {
                    if (trs.transform)
                        trs.transform.localRotation = Gallop.Math.FromMayaEuler(RotationRecorder[trs.transform]);
                }
            };
            applyRotation.Invoke(BaseLEyeBrowMorph.trsArray);
            applyRotation.Invoke(BaseREyeBrowMorph.trsArray);
        }

        public void ApplyRotationEar()
        {
            Action<List<TrsArray>> applyRotation = delegate (List<TrsArray> trsArray)
            {
               foreach (TrsArray trs in trsArray)
               {
                   if (trs.transform)
                   {
                       if (RotationRecorder.ContainsKey(trs.transform))
                       {
                            //Debug.Log("Key Found:" + trs.transform.name);
                            trs.transform.localRotation = Gallop.Math.FromMayaEuler(RotationRecorder[trs.transform]);
                        }
                        else
                        {
                            //Debug.Log("No Key Found:" + trs.transform.name);
                        }
                    }   
                }
            };
            applyRotation.Invoke(BaseLEarMorph.trsArray);
            applyRotation.Invoke(BaseREarMorph.trsArray);
        }

        public void ChangeMorphWeight(FacialMorph morph, float val)
        {
            Container.isAnimatorControl = false;
            morph.weight = val;
            ChangeMorph();
        }

        public void ChangeMorphWeight(BindProperty property, float val, FacialMorph morph, bool autoHideEye = true)
        {
            Container.isAnimatorControl = false;
            property.Value = val < 0 ? 0 : val;
            ChangeMorphEffect();

            if (autoHideEye && property.Type == BindProperty.BindType.Enable && morph.name.Contains("Manga"))
            {
                EyeMorphs[42].weight = (val > 0 ? 1 : 0);
                EyeMorphs[43].weight = (val > 0 ? 1 : 0);
                ChangeMorphEye();
            }
        }

        public void SetEyeRange(float lx, float ly, float rx, float ry)
        {
            LeftEyeXrange.weight = lx;
            LeftEyeYrange.weight = ly;
            RightEyeXrange.weight = rx;
            RightEyeYrange.weight = ry;
            if (Container.FaceOverrideData && Container.FaceOverrideData.Enable)
            {
                ChangeMorph();
            }
            else
            {
                ChangeMorphEye();
            }
        }

        public void ProcessLocator()
        {
            EyeBrowMorphs.ForEach(morph =>
            {
                if (morph.locator)
                    morph.weight = morph.locator.transform.localPosition.x * -100;
            });

            EarMorphs.ForEach(morph =>
            {
                if (morph.locator)
                    morph.weight = morph.locator.transform.localPosition.x * -100;
            });

            MouthMorphs.ForEach(morph =>
            {
                if (morph.locator)
                {
                    morph.weight = morph.locator.transform.localPosition.x * -100;
                    if (morph.weight < 0)
                        morph.weight = -morph.weight;
                }
            });

            EyeMorphs.ForEach(morph =>
            {
                if (!(morph == LeftEyeXrange || morph == LeftEyeYrange || morph == RightEyeXrange || morph == RightEyeYrange))
                {
                    if (morph.locator)
                        morph.weight = morph.locator.transform.localPosition.x * -100;
                }
            });

            OtherMorphs.ForEach(morph =>
            {
                morph.BindProperties.ForEach(property =>
                {
                    property.Value = morph.GetLocatorValue(property.Part);
                });
            });

            LeftEyeXrange.weight = -(EyeballCtrl_L_Locator.transform.localPosition.x * -100 + EyeballCtrl_All_Locator.transform.localPosition.x * -100);
            RightEyeXrange.weight = -(EyeballCtrl_R_Locator.transform.localPosition.x * -100 + EyeballCtrl_All_Locator.transform.localPosition.x * -100);

            LeftEyeYrange.weight = -(EyeballCtrl_L_Locator.transform.localPosition.y * -100 + EyeballCtrl_All_Locator.transform.localPosition.y * -100);
            RightEyeYrange.weight = (EyeballCtrl_R_Locator.transform.localPosition.y * -100 + EyeballCtrl_All_Locator.transform.localPosition.y * -100);

            ChangeMorph();
        }

        public void ResetLocator()
        {
            Container.UmaFaceAnimator.Rebind();
            ProcessLocator();
            ChangeMorph();
        }

        public void OverrideFace(FaceOverrideData overrideData)
        {

            Action<List<FacialMorph>, FaceOverrideReplaceDataSet[]> action = delegate (List<FacialMorph> morphs, FaceOverrideReplaceDataSet[] datalist)
            {
                Dictionary<LiveTimelineDefine.FacialEyeId, FacialMorph> morphDic = new Dictionary<LiveTimelineDefine.FacialEyeId, FacialMorph>();
                morphs.ForEach(m => {
                    m.OverrideType = OverrideType.Ignore;
                    m.overrideWeight = 0; 
                    morphDic.Add((LiveTimelineDefine.FacialEyeId)m.index, m); 
                });

                foreach (var data in datalist)
                {
                    bool changed = false;
                    foreach (var arr in data.DataArray)
                    {
                        foreach (var src in arr.SrcArray)
                        {
                            if (src == LiveTimelineDefine.FacialEyeId.Base) continue;
                            if (morphDic[src].weight > 0 )
                            {
                                foreach (var dst in arr.DstArray)
                                {
                                    var dm = morphDic[dst.Index];
                                    var weight = (dst.Weight == 1 ? 1 : Mathf.Lerp(0, dst.Weight, morphDic[src].weight));
                                    dm.overrideWeight += weight;
                                    dm.OverrideType =  OverrideType.Override;
                                }
                                morphDic[src].OverrideType = OverrideType.Override;
                                changed = true;
                                break;
                            }
                        }
                    }

                    if (!changed && data.IsOnlyBaseReplace) 
                    {
                        var replacemorph = morphDic[data.BaseReplaceFaceType];
                        replacemorph.overrideWeight += 1;
                        replacemorph.OverrideType = OverrideType.Override;
                    }
                }
                morphs.ForEach(m=> { m.overrideWeight = Mathf.Clamp01(m.overrideWeight); });
            };

            if (overrideData.IsBothEyesSetting)
            {
                action.Invoke(leftEyeMorph, overrideData.FaceOverrideArray);
                action.Invoke(rightEyeMorph, overrideData.FaceOverrideArray);
            }
        }

        // Lip-sync key -> LiveFaceController.AlterUpdateAutoLip (game mouth state machine). Replaces the viewer's
        // CalcMorphWeight key-to-key interpolation (AGENTS.md 8.2).
        public void AlterUpdateAutoLip(LiveTimelineKeyLipSyncData keyData_, int charaIndex, float duration)
        {
            if (_liveFaceController == null || keyData_ == null) return;
            _liveFaceController.AlterUpdateAutoLip(keyData_, charaIndex, duration);
        }

        public bool lockMouth = false;

        // Port of the Live facial update path (LiveFaceController.AlterUpdateFacialNew RVA 0x4EC20E0) in the
        // game's order: face key block -> SetMouth -> UpdateEye -> eyebrow key block -> SetEyebrow -> ear key
        // block -> SetEar -> effect key block. The first seven steps are ported in
        // LiveFaceController.AlterUpdateFacialNew_FaceParts / AlterUpdateFacialNew_Ear (which drive the
        // additive morphs through the FaceParts state machine and the DrivenKeyComponent weight ramps); the
        // effect key block below keeps the viewer's cheek / manga / tear / shade handling (the game drives
        // ModelController.SetCheek / PlayEyeHighlightAnimation / SetVisibleTear / SetMangame /
        // SetFaceShadowVisible there - reported, not ported).
        public void AlterUpdateFacialNew(ref FacialDataUpdateInfo updateInfo_, float liveTime_)
        {
            EnsureLiveFaceController();

            // DrivenKeyComponent.BlendProgrammable (RVA 0x4AE9C90) ear ramp. The game ramps the weights in
            // DrivenKeyComponent.PostJob after the timeline key update; the viewer has no driven-key job
            // pipeline, so the ramp runs here with the live timeline delta (documented adaptation,
            // EarWeightGroup header).
            float earDeltaTime = _earPrevLiveTime < 0f ? 0f : (liveTime_ - _earPrevLiveTime);
            // Delta of the ear / face weight ramps. Equal to earDeltaTime, except right after a backward seek (see below).
            float rampDeltaTime;
            if (earDeltaTime < 0f)
            {
                // Viewer-only: the game never seeks backwards (LiveFaceController.ResetEar / EarWeightGroup.Reset).
                _liveFaceController.ResetEar();
                _earWeightGroup.Reset();
                // Viewer-only, same reason: the random ear clip and its frame dedupe restart (ResetFrameEar state).
                _earDrivenKey?.ResetEar();
                _liveEarMotion?.ResetFrameEar();
                earDeltaTime = 0f;
                // The reset ramps restart from zero and, with the show paused after the seek, the timeline delta stays 0, so
                // the ears / face would never reach the weights the keys give at the new time. One long step (longer than any
                // ramp) lands them on their targets, as if the show had been playing there.
                rampDeltaTime = 10f;
            }
            else
            {
                rampDeltaTime = earDeltaTime;
            }
            _earPrevLiveTime = liveTime_;

            // Facial delegate (Director.<InitializeTimeline>b__476_3 @0x7ffd59204230), run just before
            // AlterUpdateFacialNew: no ear key -> LiveModelController.ResetFrameEar, else
            // Director.UpdateRandomEarMotion(key, liveModelController, liveFaceController). Kept here (after the seek
            // reset above) because the viewer creates the facial controller lazily in this method.
            if (updateInfo_.ear == null)
            {
                _liveEarMotion?.ResetFrameEar();
            }
            else
            {
                Gallop.Live.Director.UpdateRandomEarMotion(updateInfo_.ear, GetLiveEarMotion(), _liveFaceController);
            }

            // Game order in LiveFaceController.AlterUpdateFacialNew (RVA 0x4EC20E0): face key block -> SetMouth
            // -> UpdateEye -> eyebrow key block -> SetEyebrow -> ear key block -> SetEar -> effect key block.
            // frameRate = 0x3c (the game passes 60).
            _liveFaceController.AlterUpdateFacialNew_FaceParts(ref updateInfo_, liveTime_, 60);

            // The invented lockMouth gate (exact-match attribute == 131072 on the mouth key) is removed per
            // AGENTS.md 8.2: the game gates auto-lip with LiveFaceController._isLipSync (0xC5, set by the face /
            // mouth key blocks), which the port fills faithfully (LiveFaceController.AlterUpdateAutoLip
            // RVA 0x4EC1FD0 reads it).
            lockMouth = !_liveFaceController._isLipSync;

            _liveFaceController.AlterUpdateFacialNew_Ear(ref updateInfo_);

            // Effect key block (game order: last). The viewer maps the effect key onto the cheek / manga / tear
            // / shade morphs.
            var HasManga = false;
            if (updateInfo_.effect != null)
            {
                var info = updateInfo_.effect;
                if (CheekMorph != null)
                {
                    var cheektype = info.cheekType;
                    for (int i = 0; i < CheekMorph.BindProperties.Count; i++)
                    {
                        CheekMorph.BindProperties[i].Value = (cheektype - 1 == i ? 1 : 0);
                    }
                }

                if (MangaMorph != null)
                {
                    var mangatype = info.mangameIndex;
                    if (mangatype == 0)
                    {
                        HasManga = false;
                        MangaMorph.BindProperties[1].Value = mangatype;
                    }
                    else
                    {
                        HasManga = true;
                        MangaMorph.BindProperties[0].Value = mangatype - 1;
                        MangaMorph.BindProperties[1].Value = 1;
                    }
                }

                // LiveFaceController.AlterUpdateFacialNew @0x7ffd592420e0, effect key block:
                //  - kAttrTeary (0x20000): eye highlight animation EYE_HIGHLIGHT_ANIMATION_ARRAY[tearyType]
                //    ({NoAnim, Teary, Twinkle}); tearyType < 1 stops it, a new id plays it
                //    (ModelController.PlayEyeHighlightAnimation @0x7ffd59678420).
                //  - kAttrTearful (0x40000): SetVisibleTear(tearfulType > 0) (the viewer used tearyType here).
                if (((int)info.attribute & LiveTimelineKeyFacialEffectData.kAttrTeary) != 0 && EyeHighlight != null)
                {
                    int tearyType = info.tearyType;
                    if (tearyType < 1)
                    {
                        if (EyeHighlight.IsPlayAnimation())
                        {
                            EyeHighlight.StopAnimation();
                            ChangeMorphEye(); // parts back to the morph pose after WriteDefault
                        }
                    }
                    else if (tearyType != _prevTearyType && tearyType < 3)
                    {
                        // ModelController.PlayEyeHighlightAnimation @0x7ffd59678420 stores the key's comparison type
                        // (key +0x34) into the controller (+0x98), then PlayAnimation(id, noInterp = false).
                        EyeHighlight.ComparisonType = info.TwinkleTimeComparisonType;
                        EyeHighlight.PlayAnimation(tearyType, false);
                    }
                    _prevTearyType = tearyType;
                }

                if (StaticTearMorph != null && ((int)info.attribute & LiveTimelineKeyFacialEffectData.kAttrTearful) != 0)
                {
                    float visible = info.tearfulType > 0 ? 1f : 0f;
                    StaticTearMorph.BindProperties.ForEach(p => p.Value = visible);
                }

                if (ShadeMorph != null)
                {
                    var shaderEnable = ((int)info.attribute & LiveTimelineKeyFacialEffectData.kAttrFaceShadowVisible) > 0;
                    if (shaderEnable)
                    {
                        // (Motion Probe, viewer tool) LiveProbeMotion.Delta: 1/60 s on a released step, Time.deltaTime otherwise.
                        ShadeMorph.BindProperties[0].Value = Mathf.MoveTowards(ShadeMorph.BindProperties[0].Value, 1, LiveProbeMotion.Delta(Time.deltaTime) * 2);
                    }
                    else
                    {
                        ShadeMorph.BindProperties[0].Value = 0;
                    }
                }
                ChangeMorphEffect();
            }

            // Eye-morph hide behind the manga overlay (EyeMorphs[42]/[43]). The game does this inside
            // ModelController.SetMangame (RVA 0x52FD1C0): a static FacePartsSet is applied through the
            // DrivenKeyComponent and MangameController.ShowMangame. That port is missing (the static manga-eye
            // FacePartsSet contents), so this keeps the current behaviour for that part ONLY (AGENTS.md 8.1,
            // reported as a data gap).
            if (HasManga && EyeMorphs.Count > 43)
            {
                EyeMorphs[42].weight = 1;
                EyeMorphs[43].weight = 1;
                ChangeMorphEye();
            }

            // DrivenKeyComponent.BlendProgrammable (RVA 0x4AE9C90): the ear section plus the three face-group
            // ramps (LiveFaceController.AdvanceFaceRamps), then the weights are applied to the additive morphs.
            // DrivenKeyComponent.PostJob (@0x7ffd58e702a0) order: the ear clip (UpdateEarWeight) first, then
            // BlendProgrammable.
            // EyeBlinkController.BeforeApplyDrivenKey @0x7ffd5957d350: once per frame, after the facial keys and before
            // the driven keys (the weight ramps below) are applied. Its delta is the owner's delta time, which in the game
            // is Time.deltaTime (ModelController.get_DeltaTime; _isEnabledOriginalDeltaTime is off in a live), NOT the
            // timeline time: seeking or dragging the slider must not feed the blink timer. Time.deltaTime already follows
            // the video speed (Time.timeScale). The game pauses with the components' _isPause; the viewer pauses by
            // freezing the timeline (Director.IsPlaybackPaused, also set while dragging the slider).
            bool blinkPaused = Gallop.Live.Director.IsPlaybackPaused && !LiveProbeMotion.StepActive;
            _liveFaceController.UpdateEyeBlink(LiveProbeMotion.Delta(Time.deltaTime), blinkPaused);
            _earDrivenKey?.UpdateEarLive(earDeltaTime);
            if (rampDeltaTime > 0f)
            {
                _earWeightGroup.BlendProgrammable(rampDeltaTime);
            }
            _liveFaceController.AdvanceFaceRamps(rampDeltaTime);
            // EyeHighlightPlayableAnimator time (runs with the character delta, speed 1.0).
            // The game evaluates the highlight animator every frame before the driven keys; re-run the eye pass while
            // a highlight clip plays (and once more on the frame it stops) so the parts follow the clip.
            if (EyeHighlight != null)
            {
                bool wasPlaying = EyeHighlight.IsBusy;
                EyeHighlight.UpdateAnimation(earDeltaTime);
                if (wasPlaying || EyeHighlight.IsBusy)
                    ChangeMorphEye();
            }

            ApplyEarWeights();

            // Eye track. In the game this runs in the facial delegate AFTER AlterUpdateFacialNew
            // (Director.<InitializeTimeline>b__476_3 RVA 0x4E84230): the key's rates feed
            // EyeTraceController.SetVerticalRate / SetHorizontalRate and the aim is computed by
            // EyeTraceObject.AlterLateUpdate. Both are applied here through the eye X/Y range morphs.
            if (updateInfo_.eyeTrack != null && !updateInfo_.eyeTrack.IsNop)
            {
                var eyeTrackKey = updateInfo_.eyeTrack;
                // Director.<InitializeTimeline>b__476_3 @0x7ffd59204230 (disassembly): SetDelayRate(clamp01(
                // speedRatePer * 0.3 (DAT_7ffd5a640e28) / 100)); kAttrDurationTime clear -> ClearDurationTime and
                // CharacterObject+0xbc = -1; set and a new key frame -> SetDurationTime(time * 1/60 (DAT_7ffd5a640e78),
                // interpolateType) and +0xbc = key frame.
                _eyeTrace.DelayRate = Mathf.Clamp01(eyeTrackKey.speedRatePer * 0.3f / 100f);
                _eyeTraceL.DelayRate = _eyeTrace.DelayRate;
                if (!eyeTrackKey.IsEnabledDurationTime)
                {
                    _eyeTrace.ClearDurationTime();
                    _eyeTraceL.ClearDurationTime();
                    _eyeTraceKeyFrame = -1;
                }
                else if (_eyeTraceKeyFrame != eyeTrackKey.frame)
                {
                    _eyeTrace.SetDurationTime(eyeTrackKey.time * 0.016666668f, (int)eyeTrackKey.interpolateType);
                    _eyeTraceL.SetDurationTime(eyeTrackKey.time * 0.016666668f, (int)eyeTrackKey.interpolateType);
                    _eyeTraceKeyFrame = eyeTrackKey.frame;
                }
                // EyeBlinkController.DisableDelayedTracking (while the eyes open): both EyeTraceObjects restart their
                // smoothing with a zero duration, i.e. the aim is followed without delay (the same writes as
                // EyeTraceObject.SetDurationTime(0, 0): +0x54 on, previous offset = offset, +0x58/+0x5C/+0x60 zero). The
                // blink runs after the eye track key handling above in the game's frame, hence the pending flag.
                if (_eyeTraceDisableDelayed)
                {
                    _eyeTrace.SetDurationTime(0f, 0);
                    _eyeTraceL.SetDurationTime(0f, 0);
                }
                // EyeTraceController.AfterEndCySpringSimulation @0x7ffd59582f10: while no eye has a duration time the step is
                // deltaTime / (DeltaTimeFactor (+0x50, default 1.0, ctor) / Application.targetFrameRate).
                bool durationRunning = _eyeTrace.IsEnabledDurationTime || _eyeTraceL.IsEnabledDurationTime;
                float eyeDt = durationRunning ? earDeltaTime : earDeltaTime / (1f / Application.targetFrameRate);
                // One EyeTraceObject per eye ([0] = L, [1] = R), each with its own range (EyeTraceController.Setup).
                Vector2 offL = _eyeTraceL.Advance(GetTimelineEyeTrackOffset(eyeTrackKey, true, durationRunning), eyeDt);
                Vector2 offR = _eyeTrace.Advance(GetTimelineEyeTrackOffset(eyeTrackKey, false, durationRunning), eyeDt);
                ApplyEyeTraceOffset(offL, offR);
            }
            _eyeTraceDisableDelayed = false;
        }

        // Set by EyeBlinkController.DisableDelayedTracking for the current frame (see the eye track block above).
        private bool _eyeTraceDisableDelayed;

        // Port of the key-rate handling of the facial delegate (Director.<InitializeTimeline>b__476_3
        // RVA 0x4E84230) and the aim + rate section of EyeTraceObject.AlterLateUpdate (RVA 0x52055F0):
        //   - rates: EyeTraceController.SetVerticalRate / SetHorizontalRate (RVA 0x5204780 / 0x52044A0) clamp
        //     verticalRatePer / 100 and horizontalRatePer / 100 to [-1, 1] (DAT_7ffd5a640dac = -1, DAT_7ffd5a640d6c = 1);
        //   - aim: offset = dot(locatorParent.right / up, normalize(target - locator.position)) * moveRate
        //     (EyeTraceObject..ctor 0x3ca3d70a = 0.02) * Scale (1.0), clamped to the eye range
        //     (ctor defaults -0.007..0.007 / -0.003..0.003: _rangeMinX/_rangeMaxX/_rangeMinY/_rangeMaxY);
        //   - the rates then shift the offset toward the range limits:
        //       rate > 0: value = (max - value) * rate + value;  rate < 0: value = value - (min - value) * rate.
        // The result is mapped into the eye X/Y range morph weights with the viewer's locator convention
        // (ProcessLocator: weight = locator.localPosition.x * -100).
        // The EyeTraceObject smoothing (DelayRate, SetDurationTime) and the per-eye ranges are applied by
        // EyeTraceState / GetEyeTraceRange.

        // Target offset of EyeTraceObject.AlterLateUpdate (+0xb0/+0xb4: aim + rate shift, in locator units).
        private Vector2 GetTimelineEyeTrackOffset(LiveTimelineKeyFacialEyeTrackData key, bool leftEye, bool durationRunning)
        {
            float rateH = Mathf.Clamp(key.horizontalRatePer / 100f, -1f, 1f);
            float rateV = Mathf.Clamp(key.verticalRatePer / 100f, -1f, 1f);

            Vector3 target;
            bool isDefault = ResolveEyeTarget(key, out target);
            // EyeTraceObject.AlterLateUpdate: IsDefault (+0xC0) -> the eye stays at its rest offset (no aim); otherwise the
            // direction to the target is pulled toward the locator forward only while no duration time is running.
            Vector2 result = CalcEyeTrackAimOffset(!isDefault, target, rateH, rateV, !durationRunning, GetEyeTraceRange(leftEye)) / -100f;
            return result;
        }


        // CharacterObject._eyeTargetSetter / eyeTargetObject state kept between frames (a type whose target cannot be
        // resolved leaves the previous target, as in the game where eyeTargetObject keeps its last position).
        private bool _eyeTargetKnown;
        private Vector3 _eyeTargetWorld;

        // EyeTargetPositionSetter callback (<.ctor>b__17_0 @0x7ffd59c40000): unless IsWorldDirect the eye target object is
        // put on the ray head -> target at distance Length (zero direction when closer than 1e-5), else exactly at the target.
        private Vector3 EyeTargetFromSetter(Vector3 targetWorld, bool isWorldDirect, float length)
        {
            if (isWorldDirect)
                return targetWorld;
            Vector3 head = Container.HeadBone.transform.position;
            Vector3 dir = targetWorld - head;
            float len = dir.magnitude;
            dir = len > 1e-5f ? dir / len : Vector3.zero;
            return head + dir * length;
        }

        // Port of Gallop.Live.CharacterObject.LateUpdateEyeTrace @0x7ffd5922be40 (type = key.targetType, the facial delegate
        // Director.<InitializeTimeline>b__476_3 @0x7ffd59204230 stores it in CharacterObject +0x108). Returns IsDefault
        // (EyeTraceController.set_IsDefault); `target` is the world position of the eye target object. Constants:
        // DAT_7ffd5a640d84 = 10.0 (length for camera targets), DAT_7ffd5a640d6c = 1.0.
        //   0 (ArenaOld): eyeTargetObject.localPosition = Director.GetStageArenaPosition()  (local to the model root)
        //   1 Camera, 30-33 MultiCamera1-4: setter on the camera position, Length 10
        //   2 CharaForward and every unknown type: IsDefault
        //   3-22 CharaN: setter on the head of character N-3, Length 1
        //   40 DirectPosition: setter(world = stage.position + stage.rotation * key.DirectPosition, IsWorldDirect, 1)
        //   41 Arena: eyeTargetObject.position = PositionNode.position + GetStageArenaPosition()
        private bool ResolveEyeTarget(LiveTimelineKeyFacialEyeTrackData key, out Vector3 target)
        {
            var director = Live.Director.instance;
            int type = (int)key.targetType;
            target = _eyeTargetWorld;
            bool hasLast = _eyeTargetKnown;

            if (type == 0)
            {
                target = Container.transform.TransformPoint(director.GetStageArenaPosition());
            }
            else if (type == 1)
            {
                if (director.MainCameraTransform == null)
                    return !hasLast;
                target = EyeTargetFromSetter(director.MainCameraTransform.position, false, 10f);
            }
            else if (type == 2)
            {
                return true;
            }
            else if (type == 40)
            {
                Transform stage = director._stageController != null ? director._stageController.transform : null;
                if (stage == null)
                    return !hasLast;
                Vector3 world = stage.position + stage.rotation * key.DirectPosition;
                target = EyeTargetFromSetter(world, true, 1f);
            }
            else if (type == 41)
            {
                Transform positionNode = Container.Position != null ? Container.Position : Container.transform;
                target = positionNode.position + director.GetStageArenaPosition();
            }
            else if (type >= 3 && type < 23)
            {
                int index = type - 3;
                if (index >= director.CharaContainerScript.Count || director.CharaContainerScript[index] == null ||
                    director.CharaContainerScript[index].HeadBone == null)
                    return !hasLast;
                target = EyeTargetFromSetter(director.CharaContainerScript[index].HeadBone.transform.position, false, 1f);
            }
            else if (type >= 30 && type < 34)
            {
                int index = type - 30;
                if (!director._liveTimelineControl.ExistsMultiCamera(index))
                    return true;
                target = EyeTargetFromSetter(director._liveTimelineControl.GetMultiCameraWorldPosition(index), false, 10f);
            }
            else
            {
                return true;
            }

            _eyeTargetWorld = target;
            _eyeTargetKnown = true;
            return false;
        }

        // Smoothing / duration state of Gallop.Model.Component.EyeTraceObject (AlterLateUpdate @0x7ffd595855f0,
        // SetDurationTime @0x7ffd59586350, ctor @0x7ffd59586410: DelayRate (+0x9c) = 0.85). Both eyes share the
        // viewer's single aim, so one state drives both (the game keeps one per eye with identical inputs here).
        private sealed class EyeTraceState
        {
            public float DelayRate = 0.85f;      // +0x9c
            private bool _isEnabledDurationTime; // +0x54
            public bool IsEnabledDurationTime => _isEnabledDurationTime;
            private float _durationTime;         // +0x58
            private float _elapsedTime;          // +0x5c
            private int _interpolateType;        // +0x60
            private Vector2 _prevOffset;         // +0x64 / +0x68
            private Vector2 _offset;             // +0xa8 / +0xac

            // EyeTraceObject.SetDurationTime: start from the current offset.
            public void SetDurationTime(float duration, int interpolateType)
            {
                _isEnabledDurationTime = true;
                _prevOffset = _offset;
                _durationTime = duration;
                _elapsedTime = 0f;
                _interpolateType = interpolateType;
            }

            // EyeTraceController.ClearDurationTime @0x7ffd595830d0.
            public void ClearDurationTime()
            {
                _isEnabledDurationTime = false;
                _durationTime = 0f;
                _elapsedTime = 0f;
            }

            public void Reset()
            {
                ClearDurationTime();
                _offset = Vector2.zero;
                _prevOffset = Vector2.zero;
            }

            // Offset update of AlterLateUpdate (the part after the target offset is computed).
            public Vector2 Advance(Vector2 target, float deltaTime)
            {
                if (!_isEnabledDurationTime)
                {
                    float rate = Mathf.Clamp01(deltaTime * DelayRate);
                    _offset += (target - _offset) * rate;
                    return _offset;
                }

                _elapsedTime += deltaTime;
                float ratio;
                if (_durationTime <= 0f || _durationTime <= _elapsedTime)
                {
                    _durationTime = 0f;
                    _elapsedTime = 0f;
                    _isEnabledDurationTime = false;
                    ratio = 1f;
                }
                else
                {
                    float r = _elapsedTime / _durationTime;
                    const float HalfPi = 1.5707964f; // DAT_7ffd5a651df0
                    switch (_interpolateType)
                    {
                        case 1: ratio = 1f - Mathf.Cos(r * HalfPi); break;
                        case 2: ratio = Mathf.Sin(r * HalfPi); break;
                        case 3:
                            r += r;
                            ratio = 1f <= r ? Mathf.Sin((r - 1f) * HalfPi) * 0.5f + 0.5f
                                            : (1f - Mathf.Cos(r * HalfPi)) * 0.5f;
                            break;
                        default: ratio = r; break;
                    }
                }
                ratio = Mathf.Clamp01(ratio);
                _offset = _prevOffset + (target - _prevOffset) * ratio;
                return _offset;
            }
        }

        private readonly EyeTraceState _eyeTrace = new EyeTraceState();  // R eye (EyeTraceObject[1] in the game)
        private readonly EyeTraceState _eyeTraceL = new EyeTraceState(); // L eye (EyeTraceObject[0])
        // CharacterObject +0xbc (frame of the eye-track key whose duration was started).
        private int _eyeTraceKeyFrame = -1;

        // Viewer-only (no game counterpart): the standalone viewer's "TrackTarget" eye-follow maps an aim position
        // with the game's EyeTraceObject aim math (CalcEyeTrackAimOffset at rates 0 = no shift toward the range
        // limits). Replaces the invented GetEyeTrackRotation degree clamps (/35, /25) removed per AGENTS.md 8.2.
        public Vector2 GetEyeTrackRotation(Vector3 target)
        {
            return CalcEyeTrackAimOffset(true, target, 0f, 0f);
        }

        // Eye bone displacement of the eye X/Y range morph at weight 1 (the morph key of the Eye_L / Eye_R bone in the
        // head facial_target asset, applied linearly by ProcessMorph: localPosition += _position * weight). The game's
        // DrivenKeyComponent.InitializeEyeRange @0x7ffd58e6c2e0 builds the eye limit vectors (limit - base, +0x1CC..+0x228)
        // from the same asset data; the game values (0.00572 / 0.00858 / 0.00295 / 0.00492) equal these morph vectors.
        private static bool TryGetEyeMorphVector(FacialMorph morph, out Vector3 vec)
        {
            vec = Vector3.zero;
            if (morph == null || morph.trsArray == null) return false;
            foreach (TrsArray trs in morph.trsArray)
            {
                if (trs != null && (trs._path == "Eye_L" || trs._path == "Eye_R"))
                {
                    vec = trs._position;
                    return true;
                }
            }
            return false;
        }

        // EyeTraceController.Setup @0x7ffd59584820: per eye range = limit - eye base position. X: the L eye reaches
        // +vL (inner) and -vR (outer), the R eye -vL (inner) and +vR (outer); Y: from the Y range vectors (L = upper, R = down).
        // Returns (minX, maxX, minY, maxY); falls back to the EyeTraceObject ctor defaults when the morph data is missing.
        private Vector4 GetEyeTraceRange(bool leftEye)
        {
            if (TryGetEyeMorphVector(LeftEyeXrange, out var lx) && TryGetEyeMorphVector(RightEyeXrange, out var rx) &&
                TryGetEyeMorphVector(LeftEyeYrange, out var ly) && TryGetEyeMorphVector(RightEyeYrange, out var ry))
            {
                float minY = Mathf.Min(ly.y, ry.y), maxY = Mathf.Max(ly.y, ry.y);
                return leftEye ? new Vector4(-rx.x, lx.x, minY, maxY) : new Vector4(-lx.x, rx.x, minY, maxY);
            }
            return new Vector4(-0.007f, 0.007f, -0.003f, 0.003f);
        }

        // The game puts the Eye bone at locator + offsetX * right + offsetY * up (EyeTraceObject.AlterLateUpdate). The
        // viewer drives the Eye bones with the eye range morphs (linear bone displacement), so the weight that gives the
        // same X / Y displacement is offset / (morph vector component).
        private void ApplyEyeTraceOffset(Vector2 offL, Vector2 offR)
        {
            float lx = 0f, ly = 0f, rx = 0f, ry = 0f;
            if (TryGetEyeMorphVector(LeftEyeXrange, out var vlx) && vlx.x != 0f) lx = offL.x / vlx.x;
            if (TryGetEyeMorphVector(RightEyeXrange, out var vrx) && vrx.x != 0f) rx = offR.x / vrx.x;
            if (TryGetEyeMorphVector(LeftEyeYrange, out var vly) && vly.y != 0f) ly = offL.y / vly.y;
            if (TryGetEyeMorphVector(RightEyeYrange, out var vry) && vry.y != 0f) ry = offR.y / vry.y;
            SetEyeRange(lx, ly, rx, ry);
        }

        // EyeTraceObject.AlterLateUpdate @0x7ffd595855f0 aim part. dir = normalize(target - locator position) (zero when
        // closer than DAT_7ffd5a640d38 = 1e-5); with applyRotateTowards (no duration time running) the direction is first
        // rotated toward the locator forward by at most DAT_7ffd5a8bc0a4 = 0.0872665 rad (5 degrees), a dead zone around the
        // forward direction; offset = (dot(right, dir), dot(up, dir)) * _moveRate (0.02) * Scale (1.0).
        // Viewer approximation (reported): locator position and axes are taken from the head bone (the game uses the eye
        // locator transform and its parent).
        private Vector2 CalcEyeTrackAimOffset(bool hasTarget, Vector3 target, float rateH, float rateV, bool applyRotateTowards = false, Vector4? range = null)
        {
            Vector2 offset;
            if (hasTarget)
            {
                var head = Container.HeadBone.transform;
                Vector3 dir = target - head.position;
                float dirLen = dir.magnitude;
                dir = dirLen > 1e-5f ? dir / dirLen : Vector3.zero;
                if (applyRotateTowards)
                    dir = Vector3.RotateTowards(dir, head.forward, 0.0872665f, 0f);
                offset = new Vector2(Vector3.Dot(head.right, dir), Vector3.Dot(head.up, dir)) * 0.02f;
            }
            else
            {
                offset = Vector2.zero;
            }

            Vector4 r = range ?? new Vector4(-0.007f, 0.007f, -0.003f, 0.003f);
            float kRangeMinX = r.x;
            float kRangeMaxX = r.y;
            float kRangeMinY = r.z;
            float kRangeMaxY = r.w;

            offset.x = Mathf.Clamp(offset.x, kRangeMinX, kRangeMaxX);
            offset.y = Mathf.Clamp(offset.y, kRangeMinY, kRangeMaxY);

            // EyeTraceObject.AlterLateUpdate rate shift (the key's verticalRatePer / horizontalRatePer).
            if (0f < rateV)
            {
                offset.y = (kRangeMaxY - offset.y) * rateV + offset.y;
            }
            else if (rateV < 0f)
            {
                offset.y = offset.y - (kRangeMinY - offset.y) * rateV;
            }
            if (0f < rateH)
            {
                offset.x = (kRangeMaxX - offset.x) * rateH + offset.x;
            }
            else if (rateH < 0f)
            {
                offset.x = offset.x - (kRangeMinX - offset.x) * rateH;
            }

            offset.x = Mathf.Clamp(offset.x, kRangeMinX, kRangeMaxX);
            offset.y = Mathf.Clamp(offset.y, kRangeMinY, kRangeMaxY);

            // ProcessLocator convention: morph weight = locator.localPosition.x * -100.
            return new Vector2(offset.x * -100f, offset.y * -100f);
        }

        public void SetEyeTrack(Vector2 rotation)
        {
            SetEyeRange(rotation.x, rotation.y, rotation.x, -rotation.y);
        }


        // IFacialPartsApplier: maps the game's FaceGroupType weight groups (LiveFaceController) onto the
        // viewer's additive morph lists. Morph k of each list is FaceParts id k + 1 (morph index = parts id - 1;
        // the FacePartsSetInfo.*Dic maps are identity, FacePartsSetInfo..cctor RVA 0x58F8180).
        private sealed class FacialPartsApplier : IFacialPartsApplier
        {
            private readonly FaceDrivenKeyTarget _target;

            public FacialPartsApplier(FaceDrivenKeyTarget target)
            {
                _target = target;
            }

            private List<FacialMorph> Morphs(FaceGroupType type)
            {
                switch (type)
                {
                    case FaceGroupType.EyeR: return _target.rightEyeMorph;
                    case FaceGroupType.EyeL: return _target.leftEyeMorph;
                    case FaceGroupType.EyebrowR: return _target.rightEyebrowMorph;
                    case FaceGroupType.EyebrowL: return _target.leftEyebrowMorph;
                    case FaceGroupType.Mouth: return _target.MouthMorphs;
                }
                return null;
            }

            public int GetPartsCount(FaceGroupType type)
            {
                var morphs = Morphs(type);
                return morphs != null ? morphs.Count : 0;
            }

            public void SetPartsWeights(FaceGroupType type, float[] weights, int count)
            {
                var morphs = Morphs(type);
                if (morphs == null) return;
                for (int i = 0; i < morphs.Count && i + 1 < count; i++)
                {
                    morphs[i].weight = weights[i + 1];
                }
            }

            public void DisableDelayedEyeTracking()
            {
                _target._eyeTraceDisableDelayed = true;
            }

            public bool IsEyeHighlightSuppressingBlink()
            {
                return _target.EyeHighlight != null && _target.EyeHighlight.IsSuppressEyeBlink();
            }

            // ModelController.InitEyeBlinkController @0x7ffd59674b00: IsDisableByOverride = FaceOverrideData.IsDisableEyeBlink.
            public bool IsEyeBlinkDisabledByOverride
            {
                get { return _target.Container != null && _target.Container.FaceOverrideData != null && _target.Container.FaceOverrideData.IsDisableEyeBlink; }
            }

            public void ApplyParts(FaceGroupType type)
            {
                switch (type)
                {
                    case FaceGroupType.EyeR:
                    case FaceGroupType.EyeL:
                        _target.ChangeMorphEye();
                        break;
                    case FaceGroupType.EyebrowR:
                    case FaceGroupType.EyebrowL:
                        _target.ChangeMorphEyebrow();
                        break;
                    case FaceGroupType.Mouth:
                        _target.ChangeMorphMouth();
                        break;
                }
            }
        }
    }
}
