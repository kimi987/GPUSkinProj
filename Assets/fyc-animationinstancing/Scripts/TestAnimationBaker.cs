
using Sirenix.OdinInspector;
using UnityEngine;

namespace Fyc.AnimationInstancing
{
    public class TestAnimationBaker: SerializedMonoBehaviour
    {
        public GameObject originPrefab;


        [Button]
        public void CallBake()
        {
            if (originPrefab)
            {
                //AnimationGenerator.Instance.CallBake(originPrefab);
            }
        }
    }
}