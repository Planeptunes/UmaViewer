using UnityEngine;

namespace Gallop.Live
{
    // Gallop.Live.StageFovShift: objeto del escenario que se coloca pegado a la camara del live,
    // desplazado hacia delante cuando el FOV del timeline es mas estrecho que el FOV base del
    // escenario. Lo que va colgando de el (hijos) viaja con la camara.
    //
    // Nativo (StageController.AlterLateUpdate -> StageFovShift.AlterLateUpdate, JP 0x7ffd592de150):
    //   ratio = _baseFov / camera.fieldOfView
    //   if (ratio < 1) return                 // solo cuando el FOV baja del base
    //   ratio -= 1
    //   offset = camera.forward * (ratio * _shiftPower)   (la Y del offset solo si _isShiftY)
    //   this.transform.position = camera.transform.position + offset
    //
    // No mueve la camara: mueve su propio transform. Y no toca la rotacion.
    // Los tres valores los reescribe el track Environment del cutt mientras su clave sea valida
    // (StageController.UpdateEnvironemntStageFovShift); los serializados son el valor por defecto.
    // Los nombres de los campos son los serializados en el bundle: no renombrar.
    public class StageFovShift : MonoBehaviour
    {
        // Native StageFovShift..ctor: only _baseFov is initialised (35.0f); _shiftPower and _isShiftY
        // keep their default values.
        [SerializeField] private float _baseFov = 35f;
        [SerializeField] private float _shiftPower;
        [SerializeField] private bool _isShiftY;

        // Native StageFovShift.BaseFov / ShiftPower / IsShiftY.
        public float BaseFov
        {
            get { return _baseFov; }
            set { _baseFov = value; }
        }

        public float ShiftPower
        {
            get { return _shiftPower; }
            set { _shiftPower = value; }
        }

        public bool IsShiftY
        {
            get { return _isShiftY; }
            set { _isShiftY = value; }
        }

        public void AlterLateUpdate()
        {
            Director director = Director.instance;
            if (director == null) return;

            Camera camera = director.MainRenderCamera;
            if (camera == null) return;

            float fieldOfView = camera.fieldOfView;
            // The native does not guard this; a zero FOV would leave the transform at infinity for
            // every later frame, so refuse the frame instead.
            if (fieldOfView <= 0.0001f) return;

            float ratio = _baseFov / fieldOfView;
            if (ratio < 1f) return;
            ratio -= 1f;

            Transform cameraTransform = camera.transform;
            Vector3 offset = cameraTransform.forward * (ratio * _shiftPower);
            if (!_isShiftY) offset.y = 0f;

            transform.position = cameraTransform.position + offset;
        }
    }
}
