using UnityEditor;
using UnityEngine;

namespace AnimationInstancing
{
    [CustomEditor(typeof(AnimationInstancing))]
    public class AnimationInstancingEditor:Editor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            if (GUILayout.Button("OpenAnimationGenerator"))
            {
                GameObject prototype = ((AnimationInstancing)target).prototype;
                AnimationGenerator.Instance.MakeWindow(prototype);
            }
        }
        
    }
}