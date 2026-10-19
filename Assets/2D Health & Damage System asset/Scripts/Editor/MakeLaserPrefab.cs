#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

// Unity の上のメニュー「Battle doom > Bullet_Laser を作る」を押すと、
// Assets/Prefabs/Bullet/Bullet_Laser.prefab を自動で作る
// (このファイルは Assets/Editor フォルダに入れる。ゲームには入らない)
public static class MakeLaserPrefab
{
    [MenuItem("Battle doom/Bullet_Laser を作る")]
    static void Make()
    {
        const string folder = "Assets/Prefabs/Bullet";
        const string path = folder + "/Bullet_Laser.prefab";

        if (!AssetDatabase.IsValidFolder(folder))
        {
            Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
        }

        GameObject go = new GameObject("Bullet_Laser");
        go.AddComponent<LaserBeam>();
        WeaponInfo info = go.AddComponent<WeaponInfo>();
        info.displayName = "LASER";
        info.heatPerShot = 45f;
        info.fireInterval = 1.2f;

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);

        Selection.activeObject = prefab;
        EditorGUIUtility.PingObject(prefab);
        Debug.Log("Bullet_Laser を作りました: " + path);
    }
}
#endif
