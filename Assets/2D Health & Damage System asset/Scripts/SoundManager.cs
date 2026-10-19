using System.Collections.Generic;
using UnityEngine;

// 効果音を鳴らす係。シーンに置かなくても、最初に音を鳴らしたときに自動で作られる
// 音のファイルは Assets/Resources/Sounds/ に入れておく(名前で探して読み込む)
public class SoundManager : MonoBehaviour
{
    private static SoundManager instance;

    [Range(0f, 1f)] public float masterVolume = 0.8f;
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
        sm.source.PlayOneShot(clip, volume * sm.masterVolume);
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
