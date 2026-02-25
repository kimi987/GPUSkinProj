using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace AnimationInstancing
{
    public class AnimationTest : SerializedMonoBehaviour
    {
        public GameObject protoType;
        public int addNum = 1;
        public Mesh mesh;
        
        private AnimationInstancing _animationInstancing;
        public void Start()
        {
            AnimationInstancingMgr.GetInstance().Init();
            // var instancing = protoType.GetComponent<AnimationInstancing>();
            // instancing.Init();
            // instancing.CrossFade("idle", 0);
        }
        [Button]
        public void Add()
        {
            for (int i = 0; i < addNum; i++)
            {
                var instance = AnimationInstancingMgr.GetInstance().CreateInstance(protoType);
                _animationInstancing = instance.GetComponent<AnimationInstancing>();
                _animationInstancing.Init();
                AnimationInstancingMgr.Instance.AddInstance(instance);
                _animationInstancing.visible = true;
                _animationInstancing.CrossFade("idle", 0);
                if (i == 0 && _animationInstancing.lodInfo != null && _animationInstancing.lodInfo.Length > 0)
                {
                    mesh = _animationInstancing.lodInfo[0].vertexCacheList[0].mesh;
                }

                _animationInstancing.worldTransform.position = new Vector3(i % 100, 0, i / 100) * 0.1f;
            }
        }
        [Button]
        public void PlayAnimation(string animationName)
        {
            if (_animationInstancing)
            {
                _animationInstancing.PlayAnimation(animationName);
            }
        }

        private float spawTime = 0;
        private void OnGUI()
        {
            if (GUI.Button(new Rect(200, 50, 100, 50), "AddOrigin"))
            {
                float timeStart = Time.realtimeSinceStartup;
                Add();
                spawTime = Time.realtimeSinceStartup -  timeStart;
            }

            if (spawTime > 0)
            {
                GUIStyle myStyle = new GUIStyle(GUI.skin.label);
                myStyle.fontSize = 30; // 设置字体大小
                myStyle.normal.textColor = Color.red; // 顺便还可以改颜色
                GUI.Label(new Rect(200, 200, 300, 100), $"创建时间 {spawTime}", myStyle);
            }
        }
    }
}