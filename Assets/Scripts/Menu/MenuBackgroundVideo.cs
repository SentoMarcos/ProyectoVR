using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video; // Necesario para VideoPlayer, VideoClip, enums

// Pon este script en un GameObject vacío dentro del Canvas del menú.
// Crea (o usa) un RawImage a pantalla completa, un VideoPlayer y, si hace falta, un RenderTexture.
// Reproduce un video en bucle de forma silenciosa como fondo.
[
    RequireComponent(typeof(VideoPlayer))
]
public class MenuBackgroundVideo : MonoBehaviour
{
    [Header("Video")]
    [Tooltip("Clip de video a reproducir (asignarlo en el inspector). Si usas URL, déjalo vacío y usa 'videoURL'.")]
    public VideoClip videoClip;
    [Tooltip("URL/Path (StreamingAssets) del video si no usas VideoClip.")]
    public string videoURL = string.Empty;
    [Tooltip("Reproducir en bucle")] public bool loop = true;
    [Tooltip("Reproducir automáticamente al iniciar")] public bool playOnAwake = true;
    [Tooltip("Forzar sin audio para fondo")] public bool muteAudio = true;

    [Header("Salida")]
    [Tooltip("RawImage donde se pinta el video (se creará uno si no se asigna).")]
    public RawImage targetImage;
    [Tooltip("RenderTexture donde el VideoPlayer renderiza (se crea si está vacío).")]
    public RenderTexture renderTexture;
    [Tooltip("Resolución del RenderTexture si se crea automáticamente.")]
    public Vector2Int autoResolution = new Vector2Int(1280, 720);
    [Tooltip("Ajustar automáticamente el RenderTexture al tamaño de pantalla al iniciar")]
    public bool matchScreenResolution = true;

    [Header("Apariencia")]
    [Tooltip("Multiplicador de color para el video (usa < 1 en RGB para oscurecer y mejorar lectura de los botones)")]
    public Color videoTint = new Color(0.6f, 0.6f, 0.6f, 1f);
    [Tooltip("Crear y gestionar un overlay oscuro por encima del video para mejorar el contraste de los botones")] public bool useOverlayDimmer = true;
    [Range(0f,1f)] public float overlayAlpha = 0.35f;
    [Tooltip("Overlay existente (se creará uno si no se asigna)")] public Image overlayImage;

    VideoPlayer _vp;

    void Reset()
    {
        EnsureComponents();
    }

    void Awake()
    {
        EnsureComponents();
        ConfigureVideoPlayer();
        if (playOnAwake) Play();
    }

    void EnsureComponents()
    {
        // VideoPlayer
        _vp = GetComponent<VideoPlayer>();
        if (_vp == null) _vp = gameObject.AddComponent<VideoPlayer>();
        _vp.playOnAwake = false; // controlado por este script
        _vp.isLooping = loop;
        _vp.renderMode = VideoRenderMode.RenderTexture;
        _vp.aspectRatio = VideoAspectRatio.FitInside;
        _vp.audioOutputMode = VideoAudioOutputMode.None;

        // RawImage
        if (!targetImage)
        {
            var go = new GameObject("BackgroundVideo");
            go.transform.SetParent(transform, false);
            targetImage = go.AddComponent<RawImage>();
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        // RenderTexture
        if (!renderTexture)
        {
            renderTexture = new RenderTexture(Mathf.Max(16, autoResolution.x), Mathf.Max(16, autoResolution.y), 0, RenderTextureFormat.ARGB32)
            {
                name = "MenuBGVideoRT",
                useMipMap = false,
                autoGenerateMips = false,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
        }

        // Redimensionar a pantalla si está activado
        if (matchScreenResolution)
        {
            int w = Mathf.Max(16, Screen.width);
            int h = Mathf.Max(16, Screen.height);
            if (renderTexture.width != w || renderTexture.height != h)
            {
                if (renderTexture.IsCreated()) renderTexture.Release();
                renderTexture.width = w;
                renderTexture.height = h;
                renderTexture.Create();
            }
        }

        // Conectar
        _vp.targetTexture = renderTexture;
        if (targetImage)
        {
            targetImage.texture = renderTexture;
            targetImage.color = videoTint;
            targetImage.raycastTarget = false; // no bloquear clics a los botones
            // Asegurar material por defecto para que el color/tint funcione
            targetImage.material = null;
        }

        // Overlay para contraste por encima del video
        if (useOverlayDimmer)
        {
            if (!overlayImage)
            {
                var go = new GameObject("VideoDimmer");
                go.transform.SetParent(transform, false);
                overlayImage = go.AddComponent<Image>();
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
                overlayImage.raycastTarget = false;
            }
            // Asegurar que el overlay esté por encima del video
            if (overlayImage.transform.GetSiblingIndex() < (targetImage ? targetImage.transform.GetSiblingIndex() : 0))
                overlayImage.transform.SetAsLastSibling();
            var c = new Color(0f, 0f, 0f, Mathf.Clamp01(overlayAlpha));
            overlayImage.color = c;
        }
    }

    void OnValidate()
    {
        // Refrescar tint y overlay en editor cuando cambien los sliders
        if (targetImage) targetImage.color = videoTint;
        if (overlayImage) overlayImage.color = new Color(0f,0f,0f, Mathf.Clamp01(overlayAlpha));
    }

    void ConfigureVideoPlayer()
    {
        _vp.isLooping = loop;
        _vp.audioOutputMode = muteAudio ? VideoAudioOutputMode.None : VideoAudioOutputMode.AudioSource;
        if (videoClip)
        {
            _vp.source = VideoSource.VideoClip;
            _vp.clip = videoClip;
        }
        else if (!string.IsNullOrEmpty(videoURL))
        {
            _vp.source = VideoSource.Url;
            // Si es relativo, tomar de StreamingAssets
            if (!videoURL.StartsWith("http") && !System.IO.Path.IsPathRooted(videoURL))
            {
                string path = System.IO.Path.Combine(Application.streamingAssetsPath, videoURL);
                _vp.url = path;
            }
            else _vp.url = videoURL;
        }
    }

    public void Play()
    {
        if (!_vp) EnsureComponents();
        ConfigureVideoPlayer();
        _vp.Play();
    }

    public void Stop()
    {
        if (_vp && _vp.isPlaying) _vp.Stop();
    }

    public void Pause()
    {
        if (_vp) _vp.Pause();
    }
}
