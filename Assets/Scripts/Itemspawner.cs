using System.Collections.Generic;
using UnityEngine;

// 試合中に、ときどき足場の上にアイテムを出す。MatchManager が自動で付ける
public class ItemSpawner : MonoBehaviour
{
    [Tooltip("試合開始から最初のアイテムが出るまで(秒)")]
    public float firstDelay = 12f;
    [Tooltip("アイテムが出る間隔(秒)。この前後にばらつく")]
    public float interval = 15f;
    public float intervalJitter = 3f;

    private float timer;
    private bool started;

    void Update()
    {
        MatchManager mm = MatchManager.Instance;
        if (mm == null || !mm.IsFighting) return;

        if (!started)
        {
            started = true;
            timer = firstDelay;
        }

        timer -= Time.deltaTime;
        if (timer > 0f) return;
        timer = interval + Random.Range(-intervalJitter, intervalJitter);

        if (ItemPickup.Active.Count > 0) return;   // 同時に出るのは 1 つまで

        if (TryPickSpot(out Vector2 pos))
        {
            var kind = Random.value < 0.5f ? ItemPickup.Kind.Repair : ItemPickup.Kind.Coolant;
            if (mm.IsOnlineMode) mm.OnlineSpawnItem(kind, pos);   // オンライン:2人の画面に同じアイテムを出す
            else ItemPickup.Create(kind, pos);
        }
    }

    // 足場(Platform_ と MainFloor で始まる名前)の上から、ランダムに 1 か所選ぶ
    bool TryPickSpot(out Vector2 pos)
    {
        var spots = new List<Vector2>();
        foreach (Collider2D c in FindObjectsByType<Collider2D>(FindObjectsSortMode.None))
        {
            string n = c.gameObject.name;
            if (!n.StartsWith("Platform_") && !n.StartsWith("MainFloor")) continue;
            Bounds b = c.bounds;
            float x = Random.Range(b.min.x + 0.6f, b.max.x - 0.6f);
            spots.Add(new Vector2(x, b.max.y + 0.7f));
        }

        if (spots.Count == 0)
        {
            pos = Vector2.zero;
            return false;
        }
        pos = spots[Random.Range(0, spots.Count)];
        return true;
    }
}