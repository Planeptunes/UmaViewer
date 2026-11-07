using System;

namespace Gallop.Live.Cutt
{
    // Gallop.Live.Cutt.EnvironmentStageFovShiftUpdateInfo: the values the cutt Environment track hands
    // to the stage for the StageFovShift components (native AlterUpdate_Environment fires it once per
    // current key; StageController.UpdateEnvironemntStageFovShift applies them).
    [Serializable]
    public struct EnvironmentStageFovShiftUpdateInfo
    {
        public bool isValid;
        public float baseFov;
        public float shiftPower;
        public bool isShiftY;
    }

    public delegate void EnvironmentStageFovShiftDelegate(ref EnvironmentStageFovShiftUpdateInfo updateInfo);
}
