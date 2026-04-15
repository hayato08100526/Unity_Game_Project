using UnityEngine;

// ↓ここがファイル名と完全に一致している必要があります
public class GameDirector : MonoBehaviour
{
    public GameObject player2;
    public GameObject npc;

    void Start()
    {
        // 修正ポイント：TitleManagerのGameModeを参照
        if (TitleManager.GameMode == "Single")
        {
            if (player2 != null) player2.SetActive(false);
            if (npc != null) npc.SetActive(true);
        }
        else
        {
            if (player2 != null) player2.SetActive(true);
            if (npc != null) npc.SetActive(false);
        }
    }
}