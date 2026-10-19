using System.Collections.Generic;
using UnityEngine;

// 効果音を鳴らす係。シーンに置かなくても、最初に音を鳴らしたときに自動で作られる
// 音のファイルは Assets/Resources/Sounds/ に入れておく(名前で探して読み込む)
public class SoundManager : MonoBehaviour
{
    private static SoundManager instance;

    [Range(0f, 1f)] public float masterVolume = 0.8f;

    // 設定画面の音量(0〜1)。PlayerPrefs に保存して、次に起動したときも同じにする
    public static float Volume
    {
        get { return PlayerPrefs.GetFloat("volume", 0.8f); }
        set
        {
            PlayerPrefs.SetFloat("volume", Mathf.Clamp01(value));
            PlayerPrefs.Save();
        }
    }
    // BGM の音量(0〜1)。効果音とは別に保存する
    public static float MusicVolume
    {
        get { return PlayerPrefs.GetFloat("music", 0.6f); }
        set
        {
            PlayerPrefs.SetFloat("music", Mathf.Clamp01(value));
            PlayerPrefs.Save();
            if (instance != null && instance.music != null) instance.music.volume = MusicVolume * instance.musicDuck;
        }
    }

    private AudioSource music;
    private float musicDuck = 1f;   // リザルト画面などで少し小さくする

    // BGM を流す(同じ曲がもう流れていれば、そのまま続ける)。ファイルは Resources/Sounds/ に置く
    public static void PlayMusic(string name)
    {
        SoundManager sm = Get();
        AudioClip clip = sm.Load(name);
        if (clip == null) return;
        sm.musicDuck = 1f;
        sm.music.volume = MusicVolume;
        if (sm.music.clip == clip && sm.music.isPlaying) return;
        sm.music.clip = clip;
        sm.music.loop = true;
        sm.music.Play();
    }

    // BGM を少し小さくする(1 で元どおり)
    public static void DuckMusic(float amount)
    {
        SoundManager sm = Get();
        sm.musicDuck = Mathf.Clamp01(amount);
        sm.music.volume = MusicVolume * sm.musicDuck;
    }

    [Tooltip("CPU の銃声の大きさ(自分の銃声より少し小さく)")]
    [Range(0f, 1f)] public float cpuShotVolume = 0.55f;

    private AudioSource source;
    private readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
    private readonly Dictionary<string, float> lastPlayed = new Dictionary<string, float>();

    // 同じ音が一瞬で何十回も重ならないように、最短の間隔(秒)
    private const float MinInterval = 0.03f;

    static SoundManager Get()
    {
        if (instance == null)
        {
            GameObject go = new GameObject("SoundManager");
            instance = go.AddComponent<SoundManager>();
            DontDestroyOnLoad(go);
        }
        return instance;
    }

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f; // 2D の音

        music = gameObject.AddComponent<AudioSource>();
        music.playOnAwake = false;
        music.loop = true;
        music.spatialBlend = 0f;
    }

    AudioClip Load(string name)
    {
        if (!clips.TryGetValue(name, out AudioClip clip))
        {
            clip = Resources.Load<AudioClip>("Sounds/" + name);
            clips[name] = clip; // 見つからなくても null を覚えておく(何度も探さない)
        }
        return clip;
    }

    // 効果音を鳴らす。volume は 0〜1、pitchJitter は毎回の音の高さのゆらぎ
    public static void Play(string name, float volume = 1f, float pitchJitter = 0f)
    {
        SoundManager sm = Get();
        AudioClip clip = sm.Load(name);
        if (clip == null) return;

        if (sm.lastPlayed.TryGetValue(name, out float last) && Time.unscaledTime - last < MinInterval) return;
        sm.lastPlayed[name] = Time.unscaledTime;

        sm.source.pitch = 1f + (pitchJitter > 0f ? Random.Range(-pitchJitter, pitchJitter) : 0f);
        sm.source.PlayOneShot(clip, volume * Volume);
    }

    // 武器の名前から銃声を選ぶ(NORMAL → shoot_normal)。見つからなければ shoot_normal
    public static void PlayShot(string weaponName, bool isCpu)
    {
        SoundManager sm = Get();
        string key = "shoot_" + (weaponName ?? "normal").ToLower();
        if (key.StartsWith("shoot_track")) key = "shoot_tracking"; // TRACK / TRACKING どちらでも
        if (sm.Load(key) == null) key = "shoot_normal";
        Play(key, isCpu ? sm.cpuShotVolume : 1f, 0.05f);
    }
}