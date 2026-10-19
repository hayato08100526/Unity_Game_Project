using UnityEngine;

// 戦績の記録(強さごと)。パソコンに保存されるので、ゲームを閉じても残る
// MonoBehaviour ではないので、どこにも付けなくてよい
public static class Records
{
    public static readonly string[] LevelNames = { "EASY", "NORMAL", "HARD" };

    static string K(int level, string key) => "rec_" + level + "_" + key;

    public static int Wins(int lv) => PlayerPrefs.GetInt(K(lv, "wins"), 0);
    public static int Losses(int lv) => PlayerPrefs.GetInt(K(lv, "losses"), 0);
    public static int Draws(int lv) => PlayerPrefs.GetInt(K(lv, "draws"), 0);
    public static int Streak(int lv) => PlayerPrefs.GetInt(K(lv, "streak"), 0);
    public static int BestStreak(int lv) => PlayerPrefs.GetInt(K(lv, "best"), 0);
    public static float FastestWin(int lv) => PlayerPrefs.GetFloat(K(lv, "fast"), -1f);   // -1 = まだない

    public static int Played(int lv) => Wins(lv) + Losses(lv) + Draws(lv);

    public static int WinRate(int lv)
    {
        int p = Played(lv);
        return p == 0 ? 0 : Mathf.RoundToInt(Wins(lv) * 100f / p);
    }

    // 試合の結果を記録する。新記録があればその内容を返す(なければ空文字)
    public static string Report(int lv, string result, float time)
    {
        if (lv < 0 || lv > 2) return "";
        string news = "";

        if (result == "YOU WIN")
        {
            PlayerPrefs.SetInt(K(lv, "wins"), Wins(lv) + 1);
            int s = Streak(lv) + 1;
            PlayerPrefs.SetInt(K(lv, "streak"), s);
            if (s > BestStreak(lv))
            {
                PlayerPrefs.SetInt(K(lv, "best"), s);
                if (s >= 2) news = "NEW RECORD!  " + s + " WIN STREAK";
            }
            float f = FastestWin(lv);
            if (f < 0f || time < f)
            {
                PlayerPrefs.SetFloat(K(lv, "fast"), time);
                news = "NEW RECORD!  FASTEST WIN " + FormatTime(time);
            }
        }
        else if (result == "YOU LOSE")
        {
            PlayerPrefs.SetInt(K(lv, "losses"), Losses(lv) + 1);
            PlayerPrefs.SetInt(K(lv, "streak"), 0);
        }
        else
        {
            PlayerPrefs.SetInt(K(lv, "draws"), Draws(lv) + 1);
        }
        PlayerPrefs.Save();
        return news;
    }

    public static void ResetAll()
    {
        foreach (string key in new[] { "wins", "losses", "draws", "streak", "best", "fast" })
            for (int lv = 0; lv < 3; lv++) PlayerPrefs.DeleteKey(K(lv, key));
        PlayerPrefs.Save();
    }

    public static string FormatTime(float t)
    {
        if (t < 0f) return "--:--";
        int m = Mathf.FloorToInt(t / 60f), s = Mathf.FloorToInt(t % 60f);
        return m + ":" + s.ToString("00");
    }
}