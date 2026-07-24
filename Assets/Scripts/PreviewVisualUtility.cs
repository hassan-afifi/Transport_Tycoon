using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public static class PreviewVisualUtility
{
    public static readonly Color DefaultValidColor = Color.green;
    public static readonly Color DefaultInvalidColor = Color.red;

    public static void InitializePreviewObject(
        GameObject previewObject,
        List<Material> materialsOut,
        Color validColor,
        Color invalidColor,
        float alpha)
    {
        if (previewObject == null)
        {
            materialsOut?.Clear();
            return;
        }

        DisableColliders(previewObject);
        SetLayerRecursively(previewObject, LayerMask.NameToLayer("Ignore Raycast"));
        CacheAndPreparePreviewMaterials(previewObject, materialsOut);
        UpdatePreviewColor(materialsOut, validColor, invalidColor, alpha, false);
    }

    public static void DisableColliders(GameObject root)
    {
        if (root == null)
        {
            return;
        }

        foreach (Collider collider in root.GetComponentsInChildren<Collider>())
        {
            collider.enabled = false;
        }
    }

    public static void CacheAndPreparePreviewMaterials(GameObject previewObject, List<Material> materialsOut)
    {
        materialsOut.Clear();
        if (previewObject == null)
        {
            return;
        }

        Renderer[] renderers = previewObject.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
            {
                continue;
            }

            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            Material[] materials = renderer.materials;
            for (int j = 0; j < materials.Length; j++)
            {
                Material material = materials[j];
                if (material == null)
                {
                    continue;
                }

                MakeMaterialTransparent(material);
                materialsOut.Add(material);
            }
        }
    }

    public static void UpdatePreviewColor(
        List<Material> materials,
        Color validColor,
        Color invalidColor,
        float alpha,
        bool isValid)
    {
        if (materials == null || materials.Count == 0)
        {
            return;
        }

        Color color = isValid ? validColor : invalidColor;
        color.a = Mathf.Clamp01(alpha);

        for (int i = 0; i < materials.Count; i++)
        {
            Material material = materials[i];
            if (material == null)
            {
                continue;
            }

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }
        }
    }

    public static void MakeMaterialTransparent(Material material)
    {
        if (material == null)
        {
            return;
        }

        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 1f);
        }

        if (material.HasProperty("_Blend"))
        {
            material.SetFloat("_Blend", 0f);
        }

        if (material.HasProperty("_Mode"))
        {
            material.SetFloat("_Mode", 3f);
        }

        if (material.HasProperty("_AlphaClip"))
        {
            material.SetFloat("_AlphaClip", 0f);
        }

        if (material.HasProperty("_SrcBlend"))
        {
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        }

        if (material.HasProperty("_DstBlend"))
        {
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        }

        if (material.HasProperty("_ZWrite"))
        {
            material.SetInt("_ZWrite", 0);
        }

        material.DisableKeyword("_ALPHATEST_ON");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.renderQueue = (int)RenderQueue.Transparent;
    }

    public static void SetLayerRecursively(GameObject root, int layer)
    {
        if (root == null || layer < 0)
        {
            return;
        }

        root.layer = layer;
        foreach (Transform child in root.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }

    public static void DestroyPreviewObject(ref GameObject previewObject, List<Material> materials)
    {
        if (previewObject != null)
        {
            if (Application.isPlaying)
            {
                Object.Destroy(previewObject);
            }
            else
            {
                Object.DestroyImmediate(previewObject);
            }

            previewObject = null;
        }

        materials?.Clear();
    }
}
