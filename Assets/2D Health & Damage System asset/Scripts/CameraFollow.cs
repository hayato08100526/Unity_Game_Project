using System.Collections.Generic;
using UnityEngine;

// 2人(Player と CPU)が画面に収まるように、カメラが追いかけてズームする
// Main Camera に付ける
[RequireComponent(typeof(Camera))]
public class CameraFollow : MonoBehaviour
{
    [Tooltip("追いかける相手。空なら「Player」「CPU」という名前で探す")]
    public Transform[] targets;

    [Header("ズーム")]
    [Tooltip("いちばん寄ったときの大きさ")]
    public float minSize = 5f;
    [Tooltip("いちばん引いたときの大きさ")]
    public float maxSize = 9f;
    [Tooltip("2人の外側に空ける余白")]
    public float padding = 3f;

    [Header("位置")]
    [Tooltip("2人の真ん中から、少しずらして映す(上を広めに見せる)")]
    public Vector2 offset = new Vector2(0f, 1.5f);
    [Tooltip("カメラの中心がここより外に行かないようにする")]
    public Vector2 minCenter = new Vector2(-8f, 0f);
    public Vector2 maxCenter = new Vector2(8f, 6f);

    [Header("なめらかさ")]
    [Tooltip("大きいほど、すばやく追いかける")]
    public float smooth = 4f;

    private Camera cam;

    // 画面の揺れ
    private static CameraFollow instance;
    private Vector3 basePosition;
    private float shakeAmount;
    private float shakeTimer;
    private float shakeDuration;

    void Awake()
    {
        cam = GetComponent<Camera>();
        instance = this;
        basePosition = transform.position;
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    // どこからでも呼べる:画面を揺らす(強さ, 時間)
    public static void Shake(float amount, float duration)
    {
        if (instance == null) return;
        // すでに強く揺れているときは、弱い揺れで上書きしない
        if (instance.shakeTimer > 0f && instance.shakeAmount > amount) return;
        instance.shakeAmount = amount;
        instance.shakeDuration = duration;
        instance.shakeTimer = duration;
    }

    void Start()
    {
        if (targets == null || targets.Length == 0)
        {
            var list = new List<Transform>();
            foreach (string n in new[] { "Player", "CPU" })
            {
                GameObject go = GameObject.Find(n);
                if (go != null) list.Add(go.transform);
            }
            targets = list.ToArray();
        }
    }

    void LateUpdate()
    {
        bool any = false;
        Bounds b = default;

        foreach (Transform t in targets)
        {
            if (t == null || !IsInPlay(t)) continue;
            if (!any)
            {
                b = new Bounds(t.position, Vector3.zero);
                any = true;
            }
            else
            {
                b.Encapsulate(t.position);
            }
        }

        if (!any)
        {
            ApplyShake();
            return;
        }

        // 2人の真ん中(画面からはみ出しすぎないように制限)
        Vector2 center = (Vector2)b.center + offset;
        center.x = Mathf.Clamp(center.x, minCenter.x, maxCenter.x);
        center.y = Mathf.Clamp(center.y, minCenter.y, maxCenter.y);

        // 2人が収まる大きさ(縦と横の両方を見る)
        float needHeight = b.size.y / 2f + padding;
        float needWidth = (b.size.x / 2f + padding) / Mathf.Max(0.1f, cam.aspect);
        float size = Mathf.Clamp(Mathf.Max(needHeight, needWidth), minSize, maxSize);

        // なめらかに近づける
        float k = 1f - Mathf.Exp(-smooth * Time.deltaTime);
        Vector3 p = basePosition;
        basePosition = new Vector3(Mathf.Lerp(p.x, center.x, k), Mathf.Lerp(p.y, center.y, k), p.z);
        cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, size, k);

        ApplyShake();
    }

    // 追いかけた位置に、揺れの分だけずらして置く
    void ApplyShake()
    {
        Vector3 offset = Vector3.zero;
        if (shakeTimer > 0f)
        {
            shakeTimer -= Time.unscaledDeltaTime;
            float strength = shakeAmount * Mathf.Clamp01(shakeTimer / Mathf.Max(0.01f, shakeDuration));
            offset = (Vector3)(Random.insideUnitCircle * strength);
        }
        transform.position = basePosition + offset;
    }

    // KO されて消えている間は追いかけない
    bool IsInPlay(Transform t)
    {
        var rb = t.GetComponent<Rigidbody2D>();
        return rb == null || rb.simulated;
    }
}