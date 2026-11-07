using System;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    [Serializable]
    public class LiveTimelineKeyMultiCameraLayerData : LiveTimelineKeyWithInterpolate
    {
        public override LiveTimelineKeyDataType dataType => LiveTimelineKeyDataType.MultiCameraLayer;
        public Vector3 offsetMaxPosition;
        public Vector3 offsetMinPosition;
    }

    [Serializable]
    public class LiveTimelineKeyMultiCameraLayerDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyMultiCameraLayerData> { }

    [Serializable]
    public class LiveTimelineMultiCameraLayerData : ILiveTimelineGroupDataWithName
    {
        public LiveTimelineKeyMultiCameraLayerDataList keys = new LiveTimelineKeyMultiCameraLayerDataList();
        public int MultiCameraNo;
        public LiveTimelineMultiCameraLayerData() : base("MultiCameraLayer") { }
        public override ILiveTimelineKeyDataList GetKeyList() => keys;
    }

    [Serializable]
    public class LiveTimelineMultiCameraPostEffect : ILiveTimelineGroupDataWithName
    {
        public int MultiCameraNo;
        public LiveTimelineMultiCameraPostEffect(string defaultName) : base(defaultName) { }
    }

    [Serializable]
    public class LiveTimelineKeyMultiCameraPostFilmData : LiveTimelineKeyPostFilmData
    {
        public override LiveTimelineKeyDataType dataType => LiveTimelineKeyDataType.MultiCameraPostFilm;
    }

    [Serializable]
    public class LiveTimelineKeyMultiCameraPostFilmDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyMultiCameraPostFilmData> { }

    [Serializable]
    public class LiveTimelineMultiCameraPostFilmData : LiveTimelineMultiCameraPostEffect
    {
        public LiveTimelineKeyMultiCameraPostFilmDataList keys = new LiveTimelineKeyMultiCameraPostFilmDataList();
        public LiveTimelineMultiCameraPostFilmData() : base("Overlay") { }
        public override ILiveTimelineKeyDataList GetKeyList() => keys;
    }

    [Serializable]
    public class LiveTimelineKeyMultiCameraPostEffectBloomDiffusionData : LiveTimelineKeyPostEffectBloomDiffusionData
    {
        public override LiveTimelineKeyDataType dataType => LiveTimelineKeyDataType.MultiCameraPostEffectBloomDiffusion;
    }

    [Serializable]
    public class LiveTimelineKeyMultiCameraPostEffectBloomDiffusionDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyMultiCameraPostEffectBloomDiffusionData> { }

    [Serializable]
    public class LiveTimelineMultiCameraPostEffectBloomDiffusionData : LiveTimelineMultiCameraPostEffect
    {
        public LiveTimelineKeyMultiCameraPostEffectBloomDiffusionDataList keys = new LiveTimelineKeyMultiCameraPostEffectBloomDiffusionDataList();
        public LiveTimelineMultiCameraPostEffectBloomDiffusionData() : base("Bloom/Diffusion") { }
        public override ILiveTimelineKeyDataList GetKeyList() => keys;
    }

    [Serializable]
    public class LiveTimelineKeyMultiCameraPostEffectDOFData : LiveTimelineKeyPostEffectDOFData
    {
        public override LiveTimelineKeyDataType dataType => LiveTimelineKeyDataType.MultiCameraPostEffectDOF;
    }

    [Serializable]
    public class LiveTimelineKeyMultiCameraPostEffectDOFDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyMultiCameraPostEffectDOFData> { }

    [Serializable]
    public class LiveTimelineMultiCameraPostEffectDOFData : LiveTimelineMultiCameraPostEffect
    {
        public LiveTimelineKeyMultiCameraPostEffectDOFDataList keys = new LiveTimelineKeyMultiCameraPostEffectDOFDataList();
        public LiveTimelineMultiCameraPostEffectDOFData() : base("DOF") { }
        public override ILiveTimelineKeyDataList GetKeyList() => keys;
    }

    [Serializable]
    public class LiveTimelineKeyMultiCameraTiltShiftData : LiveTimelineKeyTiltShiftData
    {
        public override LiveTimelineKeyDataType dataType => LiveTimelineKeyDataType.MultiCameraTiltShift;
    }

    [Serializable]
    public class LiveTimelineKeyMultiCameraTiltShiftDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyMultiCameraTiltShiftData> { }

    [Serializable]
    public class LiveTimelineMultiCameraTiltShiftData : LiveTimelineMultiCameraPostEffect
    {
        public LiveTimelineKeyMultiCameraTiltShiftDataList keys = new LiveTimelineKeyMultiCameraTiltShiftDataList();
        public LiveTimelineMultiCameraTiltShiftData() : base("TiltShift") { }
        public override ILiveTimelineKeyDataList GetKeyList() => keys;
    }

    [Serializable]
    public class LiveTimelineKeyMultiCameraRadialBlurData : LiveTimelineKeyRadialBlurData
    {
        public override LiveTimelineKeyDataType dataType => LiveTimelineKeyDataType.MultiCameraRadialBlur;
    }

    [Serializable]
    public class LiveTimelineKeyMultiCameraRadialBlurDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyMultiCameraRadialBlurData> { }

    [Serializable]
    public class LiveTimelineMultiCameraRadialBlurData : LiveTimelineMultiCameraPostEffect
    {
        public LiveTimelineKeyMultiCameraRadialBlurDataList keys = new LiveTimelineKeyMultiCameraRadialBlurDataList();
        public LiveTimelineMultiCameraRadialBlurData() : base("RadialBlur") { }
        public override ILiveTimelineKeyDataList GetKeyList() => keys;
    }

    [Serializable]
    public class LiveTimelineKeyMultiCameraColorCorrectionData : LiveTimelineKeyColorCorrectionData
    {
        public override LiveTimelineKeyDataType dataType => LiveTimelineKeyDataType.MultiCameraColorCorrection;
    }

    [Serializable]
    public class LiveTimelineKeyMultiCameraColorCorrectionDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyMultiCameraColorCorrectionData> { }

    [Serializable]
    public class LiveTimelineMultiCameraColorCorrectionData : LiveTimelineMultiCameraPostEffect
    {
        public LiveTimelineKeyMultiCameraColorCorrectionDataList keys = new LiveTimelineKeyMultiCameraColorCorrectionDataList();
        public LiveTimelineMultiCameraColorCorrectionData() : base("ColorCorrection") { }
        public override ILiveTimelineKeyDataList GetKeyList() => keys;
    }

    [Serializable]
    public class LiveTimelineKeyMultiCameraTransmittedLightData : LiveTimelineKeyTransmittedLightData
    {
        public override LiveTimelineKeyDataType dataType => LiveTimelineKeyDataType.MultiCameraTransmittedLight;
    }

    [Serializable]
    public class LiveTimelineKeyMultiCameraTransmittedLightDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyMultiCameraTransmittedLightData> { }

    [Serializable]
    public class LiveTimelineMultiCameraTransmittedLightData : LiveTimelineMultiCameraPostEffect
    {
        public LiveTimelineKeyMultiCameraTransmittedLightDataList keys = new LiveTimelineKeyMultiCameraTransmittedLightDataList();
        public LiveTimelineMultiCameraTransmittedLightData() : base("TransmittedLight") { }
        public override ILiveTimelineKeyDataList GetKeyList() => keys;
    }
}
