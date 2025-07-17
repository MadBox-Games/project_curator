using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Ogxd.ProjectCurator
{
    public class DependencyGraphWindow : EditorWindow
    {
        [MenuItem("Window/Project Curator/Dependency Graph")]
        public static void Init()
        {
            GetWindow<DependencyGraphWindow>("Dependency Graph");
        }

        private Vector2 scrollPosition;
        private string selectedAssetPath;
        private int maxDepth = 5;
        private Dictionary<string, bool> foldoutStates = new Dictionary<string, bool>();
        private Dictionary<string, int> assetDepthMap = new Dictionary<string, int>();
        private HashSet<string> processedAssets = new HashSet<string>();

        private void OnEnable()
        {
            Selection.selectionChanged += OnSelectionChanged;
            OnSelectionChanged();
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= OnSelectionChanged;
        }

        private void OnSelectionChanged()
        {
            selectedAssetPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            foldoutStates.Clear();
            assetDepthMap.Clear();
            processedAssets.Clear();
            Repaint();
        }

        private void OnGUI()
        {
            DrawHeader();
            
            if (string.IsNullOrEmpty(selectedAssetPath) || Directory.Exists(selectedAssetPath))
            {
                EditorGUILayout.HelpBox("Select an asset to view its reference graph.", MessageType.Info);
                return;
            }

            string guid = AssetDatabase.AssetPathToGUID(selectedAssetPath);
            AssetInfo selectedAssetInfo = ProjectCurator.GetAsset(guid);
            
            if (selectedAssetInfo == null)
            {
                EditorGUILayout.HelpBox("Asset not found in Project Curator database. Please rebuild the database.", MessageType.Warning);
                if (GUILayout.Button("Rebuild Database"))
                {
                    ProjectCurator.RebuildDatabase();
                }
                return;
            }

            DrawGraph(selectedAssetInfo);
        }

        private void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Max Depth:", GUILayout.Width(70));
            maxDepth = EditorGUILayout.IntSlider(maxDepth, 1, 10);
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.Space();
            
            if (!string.IsNullOrEmpty(selectedAssetPath))
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Selected Asset:", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(Path.GetFileName(selectedAssetPath));
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space();
            }
        }

        private void DrawGraph(AssetInfo rootAsset)
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            
            // Clear tracking for new render
            assetDepthMap.Clear();
            processedAssets.Clear();
            
            // Start with root asset
            DrawAssetNode(rootAsset, 0, "");
            
            EditorGUILayout.EndScrollView();
        }

        private void DrawAssetNode(AssetInfo asset, int depth, string prefix)
        {
            if (asset == null || depth > maxDepth) return;
            
            string assetPath = AssetDatabase.GUIDToAssetPath(asset.guid);
            if (string.IsNullOrEmpty(assetPath)) return;
            
            string assetName = Path.GetFileName(assetPath);
            string nodeKey = $"{asset.guid}_{depth}_{prefix}";
            
            // Prevent infinite loops by tracking processed assets at each depth
            string depthKey = $"{asset.guid}_{depth}";
            if (processedAssets.Contains(depthKey)) return;
            processedAssets.Add(depthKey);
            
            // Calculate indentation
            GUILayout.BeginHorizontal();
            GUILayout.Space(depth * 20);
            
            // Draw connection lines
            if (depth > 0)
            {
                GUILayout.Label("└─", GUILayout.Width(20));
            }
            
            // Asset icon
            Texture icon = AssetDatabase.GetCachedIcon(assetPath);
            if (icon != null)
            {
                GUILayout.Label(icon, GUILayout.Width(16), GUILayout.Height(16));
            }
            
            // Asset name button
            if (GUILayout.Button(assetName, EditorStyles.linkLabel))
            {
                Selection.activeObject = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
            }
            
            // Build status indicator
            var content = new GUIContent(asset.IsIncludedInBuild ? ProjectIcons.LinkBlue : ProjectIcons.LinkBlack, 
                                       asset.IncludedStatus.ToString());
            GUILayout.Label(content, GUILayout.Width(16), GUILayout.Height(16));
            
            // Reference count
            int refCount = asset.referencers.Count;
            if (refCount > 0)
            {
                GUILayout.Label($"({refCount})", EditorStyles.miniLabel);
            }
            
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            
            // Show path as tooltip
            Rect lastRect = GUILayoutUtility.GetLastRect();
            EditorGUI.LabelField(lastRect, new GUIContent("", assetPath));
            
            // Draw references if within depth limit
            if (depth < maxDepth && refCount > 0)
            {
                // Use foldout for expandable references
                if (!foldoutStates.ContainsKey(nodeKey))
                {
                    foldoutStates[nodeKey] = depth < 2; // Auto-expand first 2 levels
                }
                
                GUILayout.BeginHorizontal();
                GUILayout.Space((depth + 1) * 20);
                
                if (refCount > 0)
                {
                    foldoutStates[nodeKey] = EditorGUILayout.Foldout(foldoutStates[nodeKey], 
                        $"Referenced by ({refCount})");
                }
                
                GUILayout.EndHorizontal();
                
                if (foldoutStates[nodeKey])
                {
                    // Sort referencers by name for consistent display
                    var sortedReferencers = asset.referencers
                        .Select(guid => new { guid, path = AssetDatabase.GUIDToAssetPath(guid) })
                        .Where(x => !string.IsNullOrEmpty(x.path))
                        .OrderBy(x => Path.GetFileName(x.path))
                        .ToList();
                    
                    foreach (var referencer in sortedReferencers)
                    {
                        AssetInfo referencerInfo = ProjectCurator.GetAsset(referencer.guid);
                        if (referencerInfo != null)
                        {
                            DrawAssetNode(referencerInfo, depth + 1, prefix + "  ");
                        }
                    }
                }
            }
        }
    }
} 