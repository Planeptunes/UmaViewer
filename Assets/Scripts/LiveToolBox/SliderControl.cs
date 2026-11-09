using System;
using System.Collections;
using System.Collections.Generic;
using Gallop.Live;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class SliderControl : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    public bool is_Touched = false;
    public bool is_Outed = false;

    public Button PauseButton;

    //El rotulo del boton de play/pausa. El Editor no permite escribir el glifo de pausa, asi que la
    //pausa se dibuja con una "=" GIRADA 90 grados (que se ve como dos barras verticales) y la
    //reproduccion con el triangulo sin girar. Cada pulsacion alterna entre las dos fases.
    private const string GlyphPlay = "\u25B6";   // ▶
    private const string GlyphPause = "=";       // girado 90 grados

    private const int FontPlay = 18;
    private const int FontPause = 25;

    //Fase actual: true = pausado, false = reproduciendo.
    private bool _paused;

    //Estado que tenia el show ANTES de empezar a arrastrar el slider. Se usa al soltarlo para
    //devolver el show al estado que el usuario habia elegido: si estaba pausado, sigue pausado; si
    //estaba sonando, se reanuda. Sin esto, arrastrar el slider reanudaba siempre aunque el show
    //estuviese en pausa.
    private bool _pausedBeforeDrag;

    private Text _label;
    private RectTransform _labelRect;

    private void Awake()
    {
        //Las flechas son atajos del live: sin esto, con el slider seleccionado tras un clic, Unity UI
        //tambien movia la barra con izquierda/derecha.
        _slider = GetComponent<Slider>();
        if (_slider != null)
        {
            Navigation nav = _slider.navigation;
            nav.mode = Navigation.Mode.None;
            _slider.navigation = nav;
        }

        if (PauseButton != null)
        {
            PauseButton.onClick.AddListener(OnPauseButtonClick);

            //El boton cuelga del slider: sin esto, arrastrarlo (aunque sea unos pixeles) mueve la
            //barra y, como esta a su izquierda, el valor se recortaba a 0 (el show volvia al
            //principio al reanudar). Ver SliderDragBlocker.cs.
            SliderDragBlocker.Ensure(PauseButton.gameObject);
        }

        _label = PauseButton != null ? PauseButton.GetComponentInChildren<Text>() : null;
        _labelRect = _label != null ? _label.rectTransform : null;

        //El video arranca reproduciendose.
        _paused = false;
        ApplyPhase();
    }

    //Atajos de teclado del live:
    //  Espacio          -> pausa / reanuda
    //  Izquierda/Derecha -> -1 s / +1 s (conserva el estado de pausa)
    //  Abajo/Arriba      -> -1 frame / +1 frame (pausa el show si estaba sonando)
    private const float SecondStep = 1f;
    private const float FrameStep = 1f / 60f;
    private const float RepeatDelay = 0.4f;
    private const float RepeatInterval = 1f / 20f;

    private Slider _slider;
    private KeyCode _heldKey = KeyCode.None;
    private float _nextRepeatTime;

    private void Update()
    {
        Director director = Director.instance;
        if (director == null || !director._isLiveSetup || director.IsOfflineRender || director.IsRecordVMD) return;
        if (IsTypingInInputField()) return;

        if (Input.GetKeyDown(KeyCode.Space))
        {
            TogglePause();
            return;
        }

        int seconds = PollStep(KeyCode.RightArrow, 1) + PollStep(KeyCode.LeftArrow, -1);
        int frames = PollStep(KeyCode.UpArrow, 1) + PollStep(KeyCode.DownArrow, -1);

        if (seconds != 0)
        {
            SeekBy(director, seconds * SecondStep);
        }
        if (frames != 0)
        {
            //Avanzar frame a frame solo tiene sentido con el show detenido.
            if (!_paused) ApplyPausedState(true);
            SeekBy(director, frames * FrameStep);
        }
    }

    //Devuelve `direction` cuando la tecla dispara un paso: al pulsarla y luego repetido mientras se
    //mantiene; 0 en cualquier otro caso.
    private int PollStep(KeyCode key, int direction)
    {
        if (Input.GetKeyDown(key))
        {
            _heldKey = key;
            _nextRepeatTime = Time.unscaledTime + RepeatDelay;
            return direction;
        }
        if (_heldKey == key)
        {
            if (!Input.GetKey(key))
            {
                _heldKey = KeyCode.None;
            }
            else if (Time.unscaledTime >= _nextRepeatTime)
            {
                _nextRepeatTime = Time.unscaledTime + RepeatInterval;
                return direction;
            }
        }
        return 0;
    }

    //Mueve el show `delta` segundos. Pausado: Director.Update relee la barra en su rama is_Touched.
    //Sonando: se marca is_Outed para que la rama de reanudacion recoloque el audio en el nuevo tiempo.
    private void SeekBy(Director director, float delta)
    {
        if (_slider == null) _slider = GetComponent<Slider>();
        if (_slider == null || director.totalTime <= 0f) return;

        float target = Mathf.Clamp(director._liveCurrentTime + delta, 0f, Mathf.Max(0f, director.totalTime - 0.001f));
        director._liveCurrentTime = target;
        _slider.SetValueWithoutNotify(target / director.totalTime);

        if (!_paused)
        {
            is_Touched = false;
            is_Outed = true;
        }
    }

    private static bool IsTypingInInputField()
    {
        var es = UnityEngine.EventSystems.EventSystem.current;
        if (es == null || es.currentSelectedGameObject == null) return false;
        GameObject go = es.currentSelectedGameObject;
        return go.GetComponent<InputField>() != null || go.GetComponent<TMPro.TMP_InputField>() != null;
    }

    //Punto de entrada publico: alterna pausa/reproduccion con el MISMO efecto que pulsar el boton de
    //la escena. Lo usan el propio boton y cualquier atajo, de modo que todos los caminos comparten
    //una unica implementacion y no se pueden desincronizar.
    public void TogglePause()
    {
        ApplyPausedState(!_paused);
    }

    private void OnPauseButtonClick()
    {
        TogglePause();
    }

    //Se llama tambien si el usuario arrastra el slider directamente (no solo el boton).
    public void OnPointerDown(PointerEventData eventData)
    {
        //Se recuerda el estado de reproduccion y se pausa mientras dura el arrastre (el show no debe
        //avanzar mientras el usuario busca una posicion).
        _pausedBeforeDrag = _paused;
        ApplyPausedState(true);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        //Al soltar se devuelve el show al estado que tenia antes de arrastrar: pausado -> pausado,
        //sonando -> sonando (reanudado en la posicion nueva).
        ApplyPausedState(_pausedBeforeDrag);
    }

    //Pone el estado de reproduccion a `paused` y publica las marcas que lee Director.Update:
    //  - pausado -> is_Touched = true (la rama is_Touched congela el show en la posicion actual)
    //  - sonando -> is_Outed   = true (la rama is_Outed reposiciona el audio y lo reanuda)
    //Es el unico sitio que toca `_paused`, `is_Touched` e `is_Outed` a la vez, para que no se puedan
    //desincronizar entre los tres caminos: boton, arrastre del slider y reanudacion.
    private void ApplyPausedState(bool paused)
    {
        _paused = paused;
        if (paused)
        {
            is_Touched = true;
            is_Outed = false;
        }
        else
        {
            is_Touched = false;
            is_Outed = true;
        }
        ApplyPhase();
    }

    //Pinta el rotulo segun la fase actual: texto + rotacion + tamano + estilo.
    private void ApplyPhase()
    {
        if (_label == null) return;

        if (_paused)
        {
            //Fase pausa: "=" girado 90 grados, tamano 25, negrita.
            _label.text = GlyphPause;
            _label.fontSize = FontPause;
            _label.fontStyle = FontStyle.Bold;
            if (_labelRect != null) _labelRect.localRotation = Quaternion.Euler(0f, 0f, 90f);
        }
        else
        {
            //Fase reproduccion: triangulo sin girar, tamano 18, normal.
            _label.text = GlyphPlay;
            _label.fontSize = FontPlay;
            _label.fontStyle = FontStyle.Normal;
            if (_labelRect != null) _labelRect.localRotation = Quaternion.identity;
        }
    }
}
