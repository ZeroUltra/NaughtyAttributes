﻿using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace NaughtyAttributes.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(UnityEngine.Object), true)]
    public class NaughtyInspector : UnityEditor.Editor
    {
        private List<SerializedProperty> _serializedProperties = new List<SerializedProperty>();
        private IEnumerable<FieldInfo> _nonSerializedFields;
        private IEnumerable<PropertyInfo> _nativeProperties;
        private IEnumerable<MethodInfo> _methods;
        private Dictionary<string, SavedBool> _foldouts = new Dictionary<string, SavedBool>();

        protected virtual void OnEnable()
        {
            _nonSerializedFields = ReflectionUtility.GetAllFields(
                target, f => f.GetCustomAttributes(typeof(ShowNonSerializedFieldAttribute), true).Length > 0);

            _nativeProperties = ReflectionUtility.GetAllProperties(
                target, p => p.GetCustomAttributes(typeof(ShowNativePropertyAttribute), true).Length > 0);

            _methods = ReflectionUtility.GetAllMethods(
                target, m => m.GetCustomAttributes(typeof(ButtonAttribute), true).Length > 0);
        }

        protected virtual void OnDisable()
        {
            ReorderableListPropertyDrawer.Instance.ClearCache();
        }

        public override void OnInspectorGUI()
        {
            GetSerializedProperties(ref _serializedProperties);

            bool anyNaughtyAttribute = _serializedProperties.Any(p => PropertyUtility.GetAttribute<INaughtyAttribute>(p) != null);
            if (!anyNaughtyAttribute)
            {
                DrawDefaultInspector();
            }
            else
            {
                DrawSerializedProperties();
            }

            DrawNonSerializedFields();
            DrawNativeProperties();
            DrawButtons();
        }

        protected void GetSerializedProperties(ref List<SerializedProperty> outSerializedProperties)
        {
            outSerializedProperties.Clear();
            using (var iterator = serializedObject.GetIterator())
            {
                if (iterator.NextVisible(true))
                {
                    do
                    {
                        outSerializedProperties.Add(serializedObject.FindProperty(iterator.name));
                    }
                    while (iterator.NextVisible(false));
                }
            }
        }

        protected void DrawSerializedProperties()
        {
            serializedObject.Update();

            // Draw non-grouped serialized properties
            foreach (var property in GetNonGroupedProperties(_serializedProperties))
            {
                if (property.name.Equals("m_Script", System.StringComparison.Ordinal))
                {
                    using (new EditorGUI.DisabledScope(disabled: true))
                    {
                        EditorGUILayout.PropertyField(property);
                    }
                }
                else
                {
                    NaughtyEditorGUI.PropertyField_Layout(property, includeChildren: true);
                }
            }

            // Draw grouped serialized properties
            foreach (var group in GetGroupedProperties(_serializedProperties))
            {
                IEnumerable<SerializedProperty> visibleProperties = group.Where(p => PropertyUtility.IsVisible(p));
                if (!visibleProperties.Any())
                {
                    continue;
                }

                NaughtyEditorGUI.BeginBoxGroup_Layout(group.Key);
                foreach (var property in visibleProperties)
                {
                    NaughtyEditorGUI.PropertyField_Layout(property, includeChildren: true);
                }

                NaughtyEditorGUI.EndBoxGroup_Layout();
            }

            // Draw foldout serialized properties
            foreach (var group in GetFoldoutProperties(_serializedProperties))
            {
                IEnumerable<SerializedProperty> visibleProperties = group.Where(p => PropertyUtility.IsVisible(p));
                if (!visibleProperties.Any())
                {
                    continue;
                }

                if (!_foldouts.ContainsKey(group.Key))
                {
                    _foldouts[group.Key] = new SavedBool($"{target.GetInstanceID()}.{group.Key}", false);
                }

                _foldouts[group.Key].Value = EditorGUILayout.Foldout(_foldouts[group.Key].Value, group.Key, true);
                if (_foldouts[group.Key].Value)
                {
                    EditorGUI.indentLevel++;
                    foreach (var property in visibleProperties)
                    {
                        NaughtyEditorGUI.PropertyField_Layout(property, true);
                    }
                    EditorGUI.indentLevel--;
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        protected void DrawNonSerializedStructOrField(object target, FieldInfo field)
        {
            if (field.FieldType.IsValueType && !field.FieldType.IsPrimitive && !field.FieldType.IsEnum && field.FieldType != typeof(LayerMask))
            {
                object subtarget = field.GetValue(target);

                if (!_foldouts.ContainsKey(field.Name))
                    _foldouts[field.Name] = new SavedBool($"{target.GetHashCode()}.{field.Name}", false);

                _foldouts[field.Name].Value = EditorGUILayout.Foldout(_foldouts[field.Name].Value, ObjectNames.NicifyVariableName(field.Name), true);
                if (_foldouts[field.Name].Value)
                {
                    EditorGUI.indentLevel++;
                    foreach (var subfield in field.FieldType.GetFields())
                    {
                        if (subfield.FieldType.IsValueType && !subfield.FieldType.IsPrimitive && !subfield.FieldType.IsEnum && subfield.FieldType != typeof(LayerMask))
                            DrawNonSerializedStructOrField(subtarget, subfield);
                        else
                        {
                            EditorGUI.BeginChangeCheck();
                            NaughtyEditorGUI.NonSerializedField_Layout(subtarget, subfield);
                            if (EditorGUI.EndChangeCheck() && target != null && field != null)
                                field.SetValue(target, subtarget);
                        }
                    }
                    EditorGUI.indentLevel--;
                }
            }
            else if (target is Object targetObject)
                NaughtyEditorGUI.NonSerializedField_Layout(targetObject, field);
            else
                NaughtyEditorGUI.NonSerializedField_Layout(target, field);
        }

        protected void DrawNonSerializedFields(bool drawHeader = false)
        {
            if (_nonSerializedFields.Any())
            {
                if (drawHeader)
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Non-Serialized Fields", GetHeaderGUIStyle());
                    NaughtyEditorGUI.HorizontalLine(
                        EditorGUILayout.GetControlRect(false), HorizontalLineAttribute.DefaultHeight, HorizontalLineAttribute.DefaultColor.GetColor());
                }

                foreach (var field in GetNonGroupedProperties(_nonSerializedFields))
                {
                    DrawNonSerializedStructOrField(serializedObject.targetObject, field);
                }

                // Draw grouped non-serialized fields
                foreach (IGrouping<string, FieldInfo> group in GetGroupedProperties(_nonSerializedFields))
                {
                    NaughtyEditorGUI.BeginBoxGroup_Layout(group.Key);
                    foreach (var field in group)
                    {
                        DrawNonSerializedStructOrField(serializedObject.targetObject, field);
                    }

                    NaughtyEditorGUI.EndBoxGroup_Layout();
                }

                // Draw foldout non-serialized fields
                foreach (IGrouping<string, FieldInfo> group in GetFoldoutProperties(_nonSerializedFields))
                {
                    if (!_foldouts.ContainsKey(group.Key))
                    {
                        _foldouts[group.Key] = new SavedBool($"{target.GetInstanceID()}.{group.Key}", false);
                    }

                    _foldouts[group.Key].Value = EditorGUILayout.Foldout(_foldouts[group.Key].Value, group.Key, true);
                    if (_foldouts[group.Key].Value)
                    {
                        EditorGUI.indentLevel++;
                        foreach (var field in group)
                        {
                            DrawNonSerializedStructOrField(serializedObject.targetObject, field);
                        }
                        EditorGUI.indentLevel--;
                    }
                }
            }
        }

        protected void DrawNativeProperties(bool drawHeader = false)
        {
            if (_nativeProperties.Any())
            {
                if (drawHeader)
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Native Properties", GetHeaderGUIStyle());
                    NaughtyEditorGUI.HorizontalLine(
                        EditorGUILayout.GetControlRect(false), HorizontalLineAttribute.DefaultHeight, HorizontalLineAttribute.DefaultColor.GetColor());
                }

                foreach (var property in _nativeProperties)
                {
                    NaughtyEditorGUI.NativeProperty_Layout(serializedObject.targetObject, property);
                }
            }
        }

        protected void DrawButtons(bool drawHeader = false)
        {
            if (_methods.Any())
            {
                if (drawHeader)
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Buttons", GetHeaderGUIStyle());
                    NaughtyEditorGUI.HorizontalLine(
                        EditorGUILayout.GetControlRect(false), HorizontalLineAttribute.DefaultHeight, HorizontalLineAttribute.DefaultColor.GetColor());
                }

                foreach (var method in _methods)
                {
                    NaughtyEditorGUI.Button(serializedObject.targetObject, method);
                }
            }
        }

        private static IEnumerable<SerializedProperty> GetNonGroupedProperties(IEnumerable<SerializedProperty> properties)
        {
            return properties.Where(p => PropertyUtility.GetAttribute<IGroupAttribute>(p) == null);
        }

        private static IEnumerable<FieldInfo> GetNonGroupedProperties(IEnumerable<FieldInfo> fieldInfos)
        {
            return fieldInfos.Where(f => PropertyUtility.GetAttribute<IGroupAttribute>(f) == null);
        }

        private static IEnumerable<IGrouping<string, SerializedProperty>> GetGroupedProperties(IEnumerable<SerializedProperty> properties)
        {
            return properties
                .Where(p => PropertyUtility.GetAttribute<BoxGroupAttribute>(p) != null)
                .GroupBy(p => PropertyUtility.GetAttribute<BoxGroupAttribute>(p).Name);
        }

        private static IEnumerable<IGrouping<string, FieldInfo>> GetGroupedProperties(IEnumerable<FieldInfo> properties)
        {
            return properties
                .Where(f => PropertyUtility.GetAttribute<BoxGroupAttribute>(f) != null)
                .GroupBy(f => PropertyUtility.GetAttribute<BoxGroupAttribute>(f).Name);
        }

        private static IEnumerable<IGrouping<string, SerializedProperty>> GetFoldoutProperties(IEnumerable<SerializedProperty> properties)
        {
            return properties
                .Where(p => PropertyUtility.GetAttribute<FoldoutAttribute>(p) != null)
                .GroupBy(p => PropertyUtility.GetAttribute<FoldoutAttribute>(p).Name);
        }

        private static IEnumerable<IGrouping<string, FieldInfo>> GetFoldoutProperties(IEnumerable<FieldInfo> properties)
        {
            return properties
                .Where(f => PropertyUtility.GetAttribute<FoldoutAttribute>(f) != null)
                .GroupBy(f => PropertyUtility.GetAttribute<FoldoutAttribute>(f).Name);
        }  

        private static GUIStyle GetHeaderGUIStyle()
        {
            GUIStyle style = new GUIStyle(EditorStyles.centeredGreyMiniLabel);
            style.fontStyle = FontStyle.Bold;
            style.alignment = TextAnchor.UpperCenter;

            return style;
        }
    }
}
