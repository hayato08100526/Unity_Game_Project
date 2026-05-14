using UnityEngine;
using UnityEngine.SceneManagement;

public class TitleManager : MonoBehaviour
{
    // これを消してしまったために他のスクリプトでエラーが出ていました
    // public static にしておくことで、どのシーンからでも参照できるようになります
    public static string GameMode = "Single";

    // 一人用ボタンから呼ぶ
    public void StartSinglePlayer()
    {
        GameMode = "Single"; // モードを記憶
        SceneManager.LoadScene("SoloScene"); // 一人用シーンへ
    }

    // 対戦用ボタンから呼ぶ
    public void StartVersusPlayer()
    {
        GameMode = "Versus"; // モードを記憶
        SceneManager.LoadScene("VersusScene"); // 対戦用シーンへ
    }
}