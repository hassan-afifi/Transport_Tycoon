using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "ObjectDatabase", menuName = "Transport Tycoon/Object Database")]
public class ObjectDatabaseSO : ScriptableObject
{
    [SerializeField] private List<ObjectData> objectsData = new();

    public IReadOnlyList<ObjectData> ObjectsData => objectsData;

    public bool TryGetObjectDataById(int id, out ObjectData objectData)
    {
        for (int i = 0; i < objectsData.Count; i++)
        {
            if (objectsData[i].ID == id)
            {
                objectData = objectsData[i];
                return true;
            }
        }

        objectData = null;
        return false;
    }

    public bool TryGetObjectDataByIndex(int index, out ObjectData objectData)
    {
        if ((uint)index >= (uint)objectsData.Count)
        {
            objectData = null;
            return false;
        }

        objectData = objectsData[index];
        return true;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        HashSet<int> seenIds = new();
        for (int i = 0; i < objectsData.Count; i++)
        {
            seenIds.Add(objectsData[i].ID);
        }
    }
#endif
}

[Serializable]
public class ObjectData
{
    [field: SerializeField] public string Name { get; private set; }
    [field: SerializeField] public int ID { get; private set; }
    [field: SerializeField] public Vector2Int Size { get; private set; } = Vector2Int.one;
    [field: SerializeField] public GameObject Prefab { get; private set; }
    [field: SerializeField] public Sprite Icon { get; private set; }

    public Vector2Int GetSizeForRotation(int rotationDegrees)
    {
        int normalizedRotation = Mathf.Abs(rotationDegrees) % 360;
        bool swapAxis = normalizedRotation == 90 || normalizedRotation == 270;
        return swapAxis ? new Vector2Int(Size.y, Size.x) : Size;
    }
}
