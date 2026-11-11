using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class UmaSceneController:MonoBehaviour
{
    public static UmaSceneController instance;
    public GameObject CavansPrefab;
    public GameObject CavansInstance;

    public GameObject LoadingProgressPanel;
    public Slider LoadingProgressSlider;
    public TextMeshProUGUI LoadingProgressText;

    private bool sceneTransitionActive;

    // True while a scene transition (and the preloads it waits for) is still running. Overlays drawn
    // above the loading screen use it to stay hidden until the scene is ready.
    public static bool IsTransitionActive => instance != null && instance.sceneTransitionActive;

    private void Awake()
    {
        if (instance)
        {
            DestroyImmediate(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(this);
    }

    private void Start()
    {
        UmaAssetManager.OnLoadProgressChange += LoadingProgressChange;
    }

    public static void LoadScene(string name, Action OnSceneloaded = null, Action OnPrevSceneUnloaded = null)
    {
        instance.StartCoroutine(instance.LoadLiveSceneAsync(name, OnSceneloaded, OnPrevSceneUnloaded));
    }

    IEnumerator LoadLiveSceneAsync(string sceneName, Action OnSceneloaded, Action OnPrevSceneUnloaded)
    {

        if (CavansInstance)
        {
            //Destroy(CavansInstance);
        }
        CavansInstance = Instantiate(CavansPrefab, transform);
        var animation = CavansInstance.GetComponent<Animation>();

        // The whole transition is one loading phase: the preload that started it hides the progress
        // panel as soon as its callback returns, and the callbacks below start further preloads
        // (Live effects) that finish after the scene swap. Keep the overlay until all of it is done.
        sceneTransitionActive = true;
        try
        {
            animation.Play("SceneTransition_s");
            yield return new WaitUntil(() => !animation.isPlaying);

            // Set the current Scene to be able to unload it later
            Scene currentScene = SceneManager.GetActiveScene();

            // The Application loads the Scene in the background at the same time as the current Scene.
            AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);

            // Wait until the last operation fully loads to return anything
            yield return new WaitUntil(()=> asyncLoad.isDone);

            OnSceneloaded?.Invoke();

            // Unload the previous Scene
            AsyncOperation asyncUnLoad = SceneManager.UnloadSceneAsync(currentScene);
            yield return new WaitUntil(() => asyncUnLoad.isDone);

            OnPrevSceneUnloaded?.Invoke();

            // A preload started by the callbacks runs its completion (Live: timeline, music, Play)
            // synchronously right after clearing LoadCoroutine, so once it is null the scene is ready.
            yield return new WaitWhile(() => UmaAssetManager.LoadCoroutine != null);
        }
        finally
        {
            sceneTransitionActive = false;
        }
        LoadingProgressChange(-1, -1);

        animation.Play("SceneTransition_e");
        yield return new WaitUntil(() => !animation.isPlaying);
        Destroy(CavansInstance);
    }

    public void LoadingProgressChange(int curren, int target, string message = null)
    {
        if(curren == -1)
        {
            // Inside a scene transition only the transition itself may hide the panel.
            if (sceneTransitionActive) return;
            LoadingProgressPanel.SetActive(false);
        }
        else if (target > 0)
        {
            LoadingProgressPanel.SetActive(true);
            LoadingProgressSlider.value = (float)curren / target;
            if (string.IsNullOrEmpty(message))
            {
                LoadingProgressText.text = $"Loading...({curren}/{target})";
            }
            else
            {
                LoadingProgressText.text = $"{message}({curren}/{target})";
            }
        }
    }
}

