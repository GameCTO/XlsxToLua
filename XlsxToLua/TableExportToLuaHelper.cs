﻿using System.Collections.Generic;
using System.Text;

public class TableExportToLuaHelper
{
    // 用于缩进lua table的字符
    private static char _LUA_TABLE_INDENTATION_CHAR = '\t';

    // 生成lua文件上方字段描述的配置
    // 每行开头的lua注释声明
    private static string _COMMENT_OUT_STRING = "-- ";
    // 变量名、数据类型、描述声明之间的间隔字符串
    private static string _DEFINE_INDENTATION_STRING = "   ";
    // dict子元素相对于父dict变量名声明的缩进字符串
    private static string _DICT_CHILD_INDENTATION_STRING = "   ";
    // 变量名声明所占的最少字符数
    private static int _FIELD_NAME_MIN_LENGTH = 30;
    // 数据类型声明所占的最少字符数
    private static int _FIELD_DATA_TYPE_MIN_LENGTH = 30;

    public static bool ExportTableToLua(TableInfo tableInfo, out string errorString)
    {
        StringBuilder content = new StringBuilder();

        // 生成数据内容开头
        content.AppendLine("return {");

        // 当前缩进量
        int currentLevel = 1;

        // 逐行读取表格内容生成lua table
        for (int row = 0; row < tableInfo.GetKeyColumnFieldInfo().Data.Count; ++row)
        {
            List<FieldInfo> allField = tableInfo.GetAllFieldInfo();

            // 将主键列作为key生成
            content.Append(_GetLuaTableIndentation(currentLevel));
            FieldInfo keyColumnField = allField[0];
            if (keyColumnField.DataType == DataType.Int)
                content.AppendFormat("[{0}]", keyColumnField.Data[row]);
            else if (keyColumnField.DataType == DataType.String)
                content.Append(keyColumnField.Data[row]);

            content.AppendLine(" = {");
            ++currentLevel;

            // 将其他列依次作为value生成
            for (int column = 1; column < allField.Count; ++column)
            {
                string oneFieldString = _GetOneField(allField[column], row, currentLevel, out errorString);
                if (errorString != null)
                {
                    errorString = string.Format("导出表格{0}失败，", tableInfo.TableName) + errorString;
                    return false;
                }
                else
                    content.Append(oneFieldString);
            }

            // 一行数据生成完毕后添加右括号结尾等
            --currentLevel;
            content.Append(_GetLuaTableIndentation(currentLevel));
            content.AppendLine("},");
        }

        // 生成数据内容结尾
        content.AppendLine("}");

        string exportString = content.ToString();
        if (AppValues.IsNeedColumnInfo == true)
            exportString = _GetColumnInfo(tableInfo) + exportString;

        // 保存为lua文件
        Utils.SaveLuaFile(tableInfo.TableName, exportString);

        errorString = null;
        return true;
    }

    /// <summary>
    /// 按配置的特殊索引导出方式输出lua文件（如果声明了在生成的lua文件开头以注释形式展示列信息，将生成更直观的嵌套字段信息，而不同于普通导出规则的列信息展示）
    /// </summary>
    public static bool SpecialExportTableToLua(TableInfo tableInfo, string exportRule, out string errorString)
    {
        exportRule = exportRule.Trim();
        // 解析按这种方式导出后的lua文件名
        int colonIndex = exportRule.IndexOf(':');
        if (colonIndex == -1)
        {
            errorString = string.Format("导出配置\"{0}\"定义错误，必须在开头声明导出lua文件名\n", exportRule);
            return false;
        }
        string fileName = exportRule.Substring(0, colonIndex).Trim();
        // 判断是否在最后的花括号内声明table value中包含的字段
        int leftBraceIndex = exportRule.LastIndexOf('{');
        int rightBraceIndex = exportRule.LastIndexOf('}');
        // 解析依次作为索引的字段名
        string indexFieldNameString = null;
        // 注意分析花括号时要考虑到未声明table value中的字段而在某索引字段完整性检查规则中用花括号声明了有效值的情况
        if (exportRule.EndsWith("}") && leftBraceIndex != -1)
            indexFieldNameString = exportRule.Substring(colonIndex + 1, leftBraceIndex - colonIndex - 1);
        else
            indexFieldNameString = exportRule.Substring(colonIndex + 1, exportRule.Length - colonIndex - 1);

        string[] indexFieldDefine = indexFieldNameString.Split(new char[] { '-' }, System.StringSplitOptions.RemoveEmptyEntries);
        // 用于索引的字段列表
        List<FieldInfo> indexField = new List<FieldInfo>();
        // 索引字段对应的完整性检查规则
        List<string> integrityCheckRules = new List<string>();
        if (indexFieldDefine.Length < 1)
        {
            errorString = string.Format("导出配置\"{0}\"定义错误，用于索引的字段不能为空，请按fileName:indexFieldName1-indexFieldName2{otherFieldName1,otherFieldName2}的格式配置\n", exportRule);
            return false;
        }
        // 检查字段是否存在且为int、float、string或lang型
        foreach (string fieldDefine in indexFieldDefine)
        {
            string fieldName = null;
            // 判断是否在字段名后用小括号声明了该字段的完整性检查规则
            int leftBracketIndex = fieldDefine.IndexOf('(');
            int rightBracketIndex = fieldDefine.IndexOf(')');
            if (leftBracketIndex > 0 && rightBracketIndex > leftBracketIndex)
            {

                fieldName = fieldDefine.Substring(0, leftBracketIndex);
                string integrityCheckRule = fieldDefine.Substring(leftBracketIndex + 1, rightBracketIndex - leftBracketIndex - 1).Trim();
                integrityCheckRules.Add(integrityCheckRule);
            }
            else
            {
                fieldName = fieldDefine.Trim();
                integrityCheckRules.Add(null);
            }

            FieldInfo fieldInfo = tableInfo.GetFieldInfoByFieldName(fieldName);
            if (fieldInfo == null)
            {
                errorString = string.Format("导出配置\"{0}\"定义错误，声明的索引字段\"{1}\"不存在\n", exportRule, fieldName);
                return false;
            }
            if (fieldInfo.DataType != DataType.Int && fieldInfo.DataType != DataType.Float && fieldInfo.DataType != DataType.String && fieldInfo.DataType != DataType.Lang)
            {
                errorString = string.Format("导出配置\"{0}\"定义错误，声明的索引字段\"{1}\"为{2}型，但只允许为int、float、string或lang型\n", exportRule, fieldName, fieldInfo.DataType);
                return false;
            }

            // 强制对string、lang型索引字段进行非空检查
            if (fieldInfo.DataType == DataType.String)
            {
                FieldCheckRule stringNotEmptyCheckRule = new FieldCheckRule();
                stringNotEmptyCheckRule.CheckType = TABLE_CHECK_TYPE.NOT_EMPTY;
                stringNotEmptyCheckRule.CheckRuleString = "notEmpty[trim]";
                TableCheckHelper.CheckNotEmpty(fieldInfo, stringNotEmptyCheckRule, out errorString);
                if (errorString != null)
                {
                    errorString = string.Format("按配置\"{0}\"进行自定义导出错误，string型索引字段\"{1}\"中存在以下空值，而作为索引的key不允许为空\n{2}\n", exportRule, fieldName, errorString);
                    return false;
                }
            }
            else if (fieldInfo.DataType == DataType.Lang)
            {
                FieldCheckRule langNotEmptyCheckRule = new FieldCheckRule();
                langNotEmptyCheckRule.CheckType = TABLE_CHECK_TYPE.NOT_EMPTY;
                langNotEmptyCheckRule.CheckRuleString = "notEmpty[key|value]";
                TableCheckHelper.CheckNotEmpty(fieldInfo, langNotEmptyCheckRule, out errorString);
                if (errorString != null)
                {
                    errorString = string.Format("按配置\"{0}\"进行自定义导出错误，lang型索引字段\"{1}\"中存在以下空值，而作为索引的key不允许为空\n{2}\n", exportRule, fieldName, errorString);
                    return false;
                }
            }

            indexField.Add(fieldInfo);
        }

        // 解析table value中要输出的字段名
        List<FieldInfo> tableValueField = new List<FieldInfo>();
        // 如果在花括号内配置了table value中要输出的字段名
        if (exportRule.EndsWith("}") && leftBraceIndex != -1 && leftBraceIndex < rightBraceIndex)
        {
            string tableValueFieldName = exportRule.Substring(leftBraceIndex + 1, rightBraceIndex - leftBraceIndex - 1);
            string[] fieldNames = tableValueFieldName.Split(new char[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (fieldNames.Length < 1)
            {
                errorString = string.Format("导出配置\"{0}\"定义错误，花括号中声明的table value中的字段不能为空，请按fileName:indexFieldName1-indexFieldName2{otherFieldName1,otherFieldName2}的格式配置\n", exportRule);
                return false;
            }
            // 检查字段是否存在
            foreach (string fieldName in fieldNames)
            {
                FieldInfo fieldInfo = tableInfo.GetFieldInfoByFieldName(fieldName);
                if (fieldInfo == null)
                {
                    errorString = string.Format("导出配置\"{0}\"定义错误，声明的table value中的字段\"{1}\"不存在\n", exportRule, fieldName);
                    return false;
                }

                if (tableValueField.Contains(fieldInfo))
                    Utils.LogWarning(string.Format("警告：导出配置\"{0}\"定义中，声明的table value中的字段存在重复，字段名为{1}（列号{2}），本工具只生成一次，请修正错误\n", exportRule, fieldName, Utils.GetExcelColumnName(fieldInfo.ColumnSeq + 1)));
                else
                    tableValueField.Add(fieldInfo);
            }
        }
        else if (exportRule.EndsWith("}") && leftBraceIndex == -1)
        {
            errorString = string.Format("导出配置\"{0}\"定义错误，声明的table value中花括号不匹配\n", exportRule);
            return false;
        }
        // 如果未在花括号内声明，则默认将索引字段之外的所有字段进行填充
        else
        {
            List<string> indexFieldNameList = new List<string>(indexFieldDefine);
            foreach (FieldInfo fieldInfo in tableInfo.GetAllFieldInfo())
            {
                if (!indexFieldNameList.Contains(fieldInfo.FieldName))
                    tableValueField.Add(fieldInfo);
            }
        }

        // 解析完依次作为索引的字段以及table value中包含的字段后，按索引要求组成相应的嵌套数据结构
        Dictionary<object, object> data = new Dictionary<object, object>();
        int rowCount = tableInfo.GetKeyColumnFieldInfo().Data.Count;
        for (int i = 0; i < rowCount; ++i)
        {
            Dictionary<object, object> temp = data;
            // 生成除最内层的数据结构
            for (int j = 0; j < indexField.Count - 1; ++j)
            {
                FieldInfo oneIndexField = indexField[j];
                var tempData = oneIndexField.Data[i];
                if (!temp.ContainsKey(tempData))
                    temp.Add(tempData, new Dictionary<object, object>());

                temp = (Dictionary<object, object>)temp[tempData];
            }
            // 最内层的value存数据的int型行号（从0开始计）
            FieldInfo lastIndexField = indexField[indexField.Count - 1];
            var lastIndexFieldData = lastIndexField.Data[i];
            if (!temp.ContainsKey(lastIndexFieldData))
                temp.Add(lastIndexFieldData, i);
            else
            {
                errorString = string.Format("错误：对表格{0}按\"{1}\"规则进行特殊索引导出时发现第{2}行与第{3}行在各个索引字段的值完全相同，导出被迫停止，请修正错误后重试\n", tableInfo.TableName, exportRule, i + AppValues.DATA_FIELD_DATA_START_INDEX + 1, temp[lastIndexFieldData]);
                Utils.LogErrorAndExit(errorString);
                return false;
            }
        }
        // 进行数据完整性检查
        if (AppValues.IsNeedCheck == true)
        {
            TableCheckHelper.CheckTableIntegrity(indexField, data, integrityCheckRules, out errorString);
            if (errorString != null)
            {
                errorString = string.Format("错误：对表格{0}按\"{1}\"规则进行特殊索引导时未通过数据完整性检查，导出被迫停止，请修正错误后重试：\n{2}\n", tableInfo.TableName, exportRule, errorString);
                return false;
            }
        }

        // 生成导出的文件内容
        StringBuilder content = new StringBuilder();

        // 生成数据内容开头
        content.AppendLine("return {");

        // 当前缩进量
        int currentLevel = 1;

        // 逐层按嵌套结构输出数据
        _GetIndexFieldData(content, data, tableValueField, ref currentLevel, out errorString);
        if (errorString != null)
        {
            errorString = string.Format("错误：对表格{0}按\"{1}\"规则进行特殊索引导出时发现以下错误，导出被迫停止，请修正错误后重试：\n{2}\n", tableInfo.TableName, exportRule, errorString);
            return false;
        }

        // 生成数据内容结尾
        content.AppendLine("}");

        string exportString = content.ToString();
        if (AppValues.IsNeedColumnInfo == true)
        {
            StringBuilder columnInfo = new StringBuilder();
            // 变量名前的缩进量
            int level = 0;

            // 按层次结构通过缩进形式生成索引列信息
            foreach (FieldInfo fieldInfo in indexField)
            {
                columnInfo.Append(_GetOneFieldColumnInfo(fieldInfo, level));
                ++level;
            }
            // 生成table value中包含字段的信息（首尾用花括号包裹）
            columnInfo.AppendLine(string.Concat(_COMMENT_OUT_STRING, _GetFieldNameIndentation(level), "{"));
            ++level;

            foreach (FieldInfo fieldInfo in tableValueField)
                columnInfo.Append(_GetOneFieldColumnInfo(fieldInfo, level));

            --level;
            columnInfo.AppendLine(string.Concat(_COMMENT_OUT_STRING, _GetFieldNameIndentation(level), "}"));

            exportString = string.Concat(columnInfo, System.Environment.NewLine, exportString);
        }

        // 保存为lua文件
        Utils.SaveLuaFile(fileName, exportString);

        errorString = null;
        return true;
    }

    /// <summary>
    /// 按指定索引方式导出数据时,通过此函数递归生成层次结构,当递归到最内层时输出指定table value中的数据
    /// </summary>
    private static void _GetIndexFieldData(StringBuilder content, Dictionary<object, object> parentDict, List<FieldInfo> tableValueField, ref int currentLevel, out string errorString)
    {
        foreach (var key in parentDict.Keys)
        {
            content.Append(_GetLuaTableIndentation(currentLevel));
            // 生成key
            if (key.GetType() == typeof(int) || key.GetType() == typeof(float))
                content.Append("[").Append(key).Append("]");
            else if (key.GetType() == typeof(string))
            {
                //// 检查作为key值的变量名是否合法
                //TableCheckHelper.CheckFieldName(key.ToString(), out errorString);
                //if (errorString != null)
                //{
                //    errorString = string.Format("作为第{0}层索引的key值不是合法的变量名，你填写的为\"{1}\"", currentLevel - 1, key.ToString());
                //    return;
                //}
                //content.Append(key);

                content.Append("[\"").Append(key).Append("\"]");
            }
            else
            {
                errorString = string.Format("SpecialExportTableToLua中出现非法类型的索引列类型{0}", key.GetType());
                Utils.LogErrorAndExit(errorString);
                return;
            }

            content.AppendLine(" = {");
            ++currentLevel;

            // 如果已是最内层，输出指定table value中的数据
            if (parentDict[key].GetType() == typeof(int))
            {
                foreach (FieldInfo fieldInfo in tableValueField)
                {
                    int rowIndex = (int)parentDict[key];
                    string oneTableValueFieldData = _GetOneField(fieldInfo, rowIndex, currentLevel, out errorString);
                    if (errorString != null)
                    {
                        errorString = string.Format("第{0}行的字段\"{1}\"（列号：{2}）导出数据错误：{3}", rowIndex + AppValues.DATA_FIELD_DATA_START_INDEX + 1, fieldInfo.FieldName, Utils.GetExcelColumnName(fieldInfo.ColumnSeq + 1), errorString);
                        return;
                    }
                    else
                        content.Append(oneTableValueFieldData);
                }
            }
            // 否则继续递归生成索引key
            else
            {
                _GetIndexFieldData(content, (Dictionary<object, object>)(parentDict[key]), tableValueField, ref currentLevel, out errorString);
                if (errorString != null)
                    return;
            }

            --currentLevel;
            content.Append(_GetLuaTableIndentation(currentLevel));
            content.AppendLine("},");
        }

        errorString = null;
    }

    /// <summary>
    /// 生成要在lua文件最上方以注释形式展示的列信息
    /// </summary>
    private static string _GetColumnInfo(TableInfo tableInfo)
    {
        // 变量名前的缩进量
        int level = 0;

        StringBuilder content = new StringBuilder();
        foreach (FieldInfo fieldInfo in tableInfo.GetAllFieldInfo())
            content.Append(_GetOneFieldColumnInfo(fieldInfo, level));

        content.Append(System.Environment.NewLine);
        return content.ToString();
    }

    private static string _GetOneFieldColumnInfo(FieldInfo fieldInfo, int level)
    {
        StringBuilder content = new StringBuilder();
        content.AppendFormat("{0}{1, -" + _FIELD_NAME_MIN_LENGTH + "}{2}{3, -" + _FIELD_DATA_TYPE_MIN_LENGTH + "}{4}{5}\n", _COMMENT_OUT_STRING, _GetFieldNameIndentation(level) + fieldInfo.FieldName, _DEFINE_INDENTATION_STRING, fieldInfo.DataTypeString, _DEFINE_INDENTATION_STRING, fieldInfo.Desc);
        if (fieldInfo.DataType == DataType.Dict || fieldInfo.DataType == DataType.Array)
        {
            ++level;
            foreach (FieldInfo childFieldInfo in fieldInfo.ChildField)
                content.Append(_GetOneFieldColumnInfo(childFieldInfo, level));

            --level;
        }

        return content.ToString();
    }

    /// <summary>
    /// 生成columnInfo变量名前的缩进字符串
    /// </summary>
    private static string _GetFieldNameIndentation(int level)
    {
        string indentationString = string.Empty;
        for (int i = 0; i < level; ++i)
            indentationString += _DICT_CHILD_INDENTATION_STRING;

        return indentationString;
    }

    private static string _GetOneField(FieldInfo fieldInfo, int row, int level, out string errorString)
    {
        StringBuilder content = new StringBuilder();
        errorString = null;

        // 变量名前的缩进
        content.Append(_GetLuaTableIndentation(level));
        // 变量名
        content.Append(fieldInfo.FieldName);
        content.Append(" = ");
        // 对应数据值
        string value = null;
        switch (fieldInfo.DataType)
        {
            case DataType.Int:
            case DataType.Float:
            case DataType.String:
            case DataType.Bool:
                {
                    value = _GetBaseValue(fieldInfo, row, level);
                    break;
                }
            case DataType.Lang:
                {
                    value = _GetLangValue(fieldInfo, row, level);
                    break;
                }
            case DataType.TableString:
                {
                    value = _GetTableStringValue(fieldInfo, row, level, out errorString);
                    break;
                }
            case DataType.Dict:
            case DataType.Array:
                {
                    value = _GetSetValue(fieldInfo, row, level);
                    break;
                }
        }

        if (errorString != null)
        {
            errorString = string.Format("第{0}行第{1}列的数据存在错误无法导出，", row + AppValues.DATA_FIELD_DATA_START_INDEX + 1, Utils.GetExcelColumnName(fieldInfo.ColumnSeq + 1)) + errorString;
            return null;
        }

        content.Append(value);
        // 一个字段结尾加逗号并换行
        content.AppendLine(",");

        return content.ToString();
    }

    private static string _GetBaseValue(FieldInfo fieldInfo, int row, int level)
    {
        StringBuilder content = new StringBuilder();

        switch (fieldInfo.DataType)
        {
            case DataType.Int:
            case DataType.Float:
                {
                    content.Append(fieldInfo.Data[row]);
                    break;
                }
            case DataType.String:
                {
                    content.Append("\"");
                    // 将单元格中填写的英文引号进行转义，使得单元格中填写123"456时，最终生成的lua文件中为xx = "123\"456"
                    content.Append(fieldInfo.Data[row].ToString().Replace("\"", "\\\""));
                    content.Append("\"");
                    break;
                }
            case DataType.Bool:
                {
                    if ((bool)fieldInfo.Data[row] == true)
                        content.Append("true");
                    else
                        content.Append("false");

                    break;
                }
            default:
                {
                    Utils.LogErrorAndExit("错误：用_WriteBaseValue函数解析了非基础类型的数据");
                    break;
                }
        }

        return content.ToString();
    }

    private static string _GetLangValue(FieldInfo fieldInfo, int row, int level)
    {
        StringBuilder content = new StringBuilder();

        if (fieldInfo.Data[row] != null)
        {
            content.Append("\"");
            content.Append(fieldInfo.Data[row].ToString().Replace("\"", "\\\""));
            content.Append("\"");
        }
        else
        {
            if (AppValues.IsPrintEmptyStringWhenLangNotMatching == true)
                content.Append("\"\"");
            else
                content.Append("nil");
        }

        return content.ToString();
    }

    private static string _GetSetValue(FieldInfo fieldInfo, int row, int level)
    {
        StringBuilder content = new StringBuilder();
        string errorString = null;

        // 如果该dict或array数据用-1标为无效，则赋值为nil
        if ((bool)fieldInfo.Data[row] == false)
            content.Append("nil");
        else
        {
            // 包裹dict或array所生成table的左括号
            content.AppendLine("{");
            ++level;
            // 逐个对子元素进行生成
            foreach (FieldInfo childField in fieldInfo.ChildField)
            {
                // 因为只有tableString型数据在导出时才有可能出现错误，而dict或array子元素不可能为tableString型，故这里不会出错
                string oneFieldString = _GetOneField(childField, row, level, out errorString);
                content.Append(oneFieldString);
            }
            // 包裹dict或array所生成table的右括号
            --level;
            content.Append(_GetLuaTableIndentation(level));
            content.Append("}");
        }

        return content.ToString();
    }

    private static string _GetTableStringValue(FieldInfo fieldInfo, int row, int level, out string errorString)
    {
        StringBuilder content = new StringBuilder();
        errorString = null;

        string inputData = fieldInfo.Data[row].ToString();

        // tableString字符串中不允许出现英文引号、斜杠
        if (inputData.Contains("\"") || inputData.Contains("\\") || inputData.Contains("/"))
        {
            errorString = "tableString字符串中不允许出现英文引号、斜杠";
            return null;
        }

        // 包裹tableString所生成table的左括号
        content.AppendLine("{");
        ++level;

        // 每组数据间用英文分号分隔，最终每组数据会生成一个lua table
        string[] allDataString = inputData.Split(new char[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries);
        // 记录每组数据中的key值（转为字符串后的），不允许出现相同的key（key：每组数据中的key值， value：第几组数据，从0开始记）
        Dictionary<string, int> stringKeys = new Dictionary<string, int>();
        for (int i = 0; i < allDataString.Length; ++i)
        {
            content.Append(_GetLuaTableIndentation(level));

            // 根据key的格式定义生成key
            switch (fieldInfo.TableStringFormatDefine.KeyDefine.KeyType)
            {
                case TABLE_STRING_KEY_TYPE.SEQ:
                    {
                        content.AppendFormat("[{0}]", i + 1);
                        break;
                    }
                case TABLE_STRING_KEY_TYPE.DATA_IN_INDEX:
                    {
                        string value = _GetDataInIndexType(fieldInfo.TableStringFormatDefine.KeyDefine.DataInIndexDefine, allDataString[i], out errorString);
                        if (errorString == null)
                        {
                            if (fieldInfo.TableStringFormatDefine.KeyDefine.DataInIndexDefine.DataType == DataType.Int)
                            {
                                // 检查key是否在该组数据中重复
                                if (stringKeys.ContainsKey(value))
                                    errorString = string.Format("第{0}组数据与第{1}组数据均为相同的key（{2}）", stringKeys[value] + 1, i + 1, value);
                                else
                                {
                                    stringKeys.Add(value, i);
                                    content.AppendFormat("[{0}]", value);
                                }
                            }
                            else if (fieldInfo.TableStringFormatDefine.KeyDefine.DataInIndexDefine.DataType == DataType.String)
                            {
                                // string型的key不允许为空或纯空格且必须符合变量名的规范
                                value = value.Trim();
                                if (TableCheckHelper.CheckFieldName(value, out errorString))
                                {
                                    // 检查key是否在该组数据中重复
                                    if (stringKeys.ContainsKey(value))
                                        errorString = string.Format("第{0}组数据与第{1}组数据均为相同的key（{2}）", stringKeys[value] + 1, i + 1, value);
                                    else
                                    {
                                        stringKeys.Add(value, i);
                                        content.Append(value);
                                    }
                                }
                                else
                                    errorString = "string型的key不符合变量名定义规范，" + errorString;
                            }
                            else
                            {
                                Utils.LogErrorAndExit("错误：用_WriteTableStringValue函数导出非int或string型的key值");
                                return null;
                            }
                        }

                        break;
                    }
                default:
                    {
                        Utils.LogErrorAndExit("错误：用_WriteTableStringValue函数导出未知类型的key");
                        return null;
                    }
            }
            if (errorString != null)
            {
                errorString = string.Format("tableString中第{0}组数据（{1}）的key数据存在错误，", i + 1, allDataString[i]) + errorString;
                return null;
            }

            content.Append(" = ");

            // 根据value的格式定义生成value
            switch (fieldInfo.TableStringFormatDefine.ValueDefine.ValueType)
            {
                case TABLE_STRING_VALUE_TYPE.TRUE:
                    {
                        content.Append("true");
                        break;
                    }
                case TABLE_STRING_VALUE_TYPE.DATA_IN_INDEX:
                    {
                        string value = _GetDataInIndexType(fieldInfo.TableStringFormatDefine.ValueDefine.DataInIndexDefine, allDataString[i], out errorString);
                        if (errorString == null)
                        {
                            DataType dataType = fieldInfo.TableStringFormatDefine.ValueDefine.DataInIndexDefine.DataType;
                            if (dataType == DataType.String || dataType == DataType.Lang)
                                content.AppendFormat("\"{0}\"", value);
                            else
                                content.Append(value);
                        }

                        break;
                    }
                case TABLE_STRING_VALUE_TYPE.TABLE:
                    {
                        content.AppendLine("{");
                        ++level;

                        // 依次输出table中定义的子元素
                        foreach (TableElementDefine elementDefine in fieldInfo.TableStringFormatDefine.ValueDefine.TableValueDefineList)
                        {
                            content.Append(_GetLuaTableIndentation(level));
                            content.Append(elementDefine.KeyName);
                            content.Append(" = ");
                            string value = _GetDataInIndexType(elementDefine.DataInIndexDefine, allDataString[i], out errorString);
                            if (errorString == null)
                            {
                                if (elementDefine.DataInIndexDefine.DataType == DataType.String || elementDefine.DataInIndexDefine.DataType == DataType.Lang)
                                    content.AppendFormat("\"{0}\"", value);
                                else
                                    content.Append(value);
                            }
                            content.AppendLine(",");
                        }
                        --level;
                        content.Append(_GetLuaTableIndentation(level));
                        content.Append("}");

                        break;
                    }
                default:
                    {
                        Utils.LogErrorAndExit("错误：用_WriteTableStringValue函数导出未知类型的value");
                        return null;
                    }
            }
            if (errorString != null)
            {
                errorString = string.Format("tableString中第{0}组数据（{1}）的value数据存在错误，", i + 1, allDataString[i]) + errorString;
                return null;
            }

            // 每组数据生成完毕后加逗号并换行
            content.AppendLine(",");
        }

        // 包裹tableString所生成table的右括号
        --level;
        content.Append(_GetLuaTableIndentation(level));
        content.Append("}");

        return content.ToString();
    }

    /// <summary>
    /// 将形如#1(int)的数据定义解析为要输出的字符串
    /// </summary>
    private static string _GetDataInIndexType(DataInIndexDefine define, string oneDataString, out string errorString)
    {
        // 一组数据中的子元素用英文逗号分隔
        string[] allElementString = oneDataString.Trim().Split(new char[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries);
        // 检查是否存在指定序号的数据
        if (allElementString.Length < define.DataIndex)
        {
            errorString = string.Format("解析#{0}({1})类型的数据错误，输入的数据中只有{2}个子元素", define.DataIndex, define.DataType.ToString(), allElementString.Length);
            return null;
        }
        // 检查是否为指定类型的合法数据
        string inputString = allElementString[define.DataIndex - 1];
        if (define.DataType != DataType.String)
            inputString = inputString.Trim();

        string value = _GetDataStringInTableString(inputString, define.DataType, out errorString);
        if (errorString != null)
        {
            errorString = string.Format("解析#{0}({1})类型的数据错误，", define.DataIndex, define.DataType.ToString()) + errorString;
            return null;
        }
        else
            return value;
    }

    /// <summary>
    /// 将tableString类型数据字符串中的某个所填数据转为需要输出的字符串
    /// </summary>
    private static string _GetDataStringInTableString(string inputData, DataType dataType, out string errorString)
    {
        string result = null;
        errorString = null;

        switch (dataType)
        {
            case DataType.Bool:
                {
                    if ("1".Equals(inputData))
                        result = "true";
                    else if ("0".Equals(inputData))
                        result = "false";
                    else
                        errorString = string.Format("输入的\"{0}\"不是合法的bool值，正确填写bool值方式为填1代表true，0代表false", inputData);

                    break;
                }
            case DataType.Int:
                {
                    int intValue;
                    bool isValid = int.TryParse(inputData, out intValue);
                    if (isValid)
                        result = intValue.ToString();
                    else
                        errorString = string.Format("输入的\"{0}\"不是合法的int类型的值", inputData);

                    break;
                }
            case DataType.Float:
                {
                    float floatValue;
                    bool isValid = float.TryParse(inputData, out floatValue);
                    if (isValid)
                        result = floatValue.ToString();
                    else
                        errorString = string.Format("输入的\"{0}\"不是合法的float类型的值", inputData);

                    break;
                }
            case DataType.String:
                {
                    result = inputData;
                    break;
                }
            case DataType.Lang:
                {
                    if (AppValues.LangData.ContainsKey(inputData))
                    {
                        string langValue = AppValues.LangData[inputData];
                        if (langValue.Contains("\"") || langValue.Contains("\\") || langValue.Contains("/") || langValue.Contains(",") || langValue.Contains(";"))
                            errorString = string.Format("tableString中的lang型数据中不允许出现英文引号、斜杠、逗号、分号，你输入的key（{0}）对应在lang文件中的值为\"{1}\"", inputData, langValue);
                        else
                            result = langValue;
                    }
                    else
                        errorString = string.Format("输入的lang型数据的key（{0}）在lang文件中找不到对应的value", inputData);

                    break;
                }
            default:
                {
                    Utils.LogErrorAndExit("错误：用_GetDataInTableString函数解析了tableString中不支持的数据类型");
                    break;
                }
        }

        return result;
    }

    private static string _GetLuaTableIndentation(int level)
    {
        return new string(_LUA_TABLE_INDENTATION_CHAR, level);
    }
}
