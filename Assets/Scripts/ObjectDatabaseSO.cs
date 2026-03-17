using UnityEngine;
using System;
using System.Collections.Generic;




[CreateAssetMenu]
public class ObjectDatabaseSO : ScriptableObject {
    public List<ObjectData> objectsData = new List<ObjectData>();
}

[Serializable]
public class ObjectData
{
    [field: SerializeField]
    public string Name{get; private set;}
    [field: SerializeField]
    public int ID {get; private set;}

    [field: SerializeField]
    public Vector2Int Size {get; private set;} = Vector2Int.one;

    [field: SerializeField]
    public GameObject Prefab {get; private set;}

    [field: SerializeField] public Sprite Icon { get; private set; } 

}


