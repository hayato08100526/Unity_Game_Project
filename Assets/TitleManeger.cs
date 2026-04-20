using UnityEngine;
using UnityEngine.SceneManagement;

public class TitleManager : MonoBehaviour
{
    // 静的な変数（Static）にすることで、画面が変わっても「どのモードか」を覚えておけるようにします
    public static string GameMode = "Single";

    // 一人用ボタンから呼ぶ機能
    public void StartSinglePlayer()
    {
        GameMode = "Single";
        SceneManager.LoadScene("SampleScene"); // 本編へ
    }

    // 対戦用ボタンから呼ぶ機能
    public void StartVersusPlayer()
    {
        GameMode = "Versus";
        SceneManager.LoadScene("SampleScene"); // 本編へ
    }
}