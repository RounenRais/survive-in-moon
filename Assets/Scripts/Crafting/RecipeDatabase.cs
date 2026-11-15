using System.Collections.Generic;
using UnityEngine;

// Every recipe the workbench can make, in the order shown. One asset at Resources/RecipeDatabase
// (made by Survive In Moon > Build Game Content).
[CreateAssetMenu(menuName = "Survive In Moon/Recipe Database", fileName = "RecipeDatabase")]
public class RecipeDatabase : ScriptableObject
{
    public List<RecipeDefinition> recipes = new List<RecipeDefinition>();

    private static RecipeDatabase instance;
    public static RecipeDatabase Instance
    {
        get
        {
            if (instance == null) instance = Resources.Load<RecipeDatabase>("RecipeDatabase");
            return instance;
        }
    }
}
