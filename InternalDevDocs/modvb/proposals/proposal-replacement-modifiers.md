# 替换修饰符 / Replacement Modifiers (`Replaceable`/`Replaces`/`MustReplace`/`NotReplaceable`)

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

本建议引入四个替换修饰符：`Replaceable`、`Replaces`、`MustReplace`、`NotReplaceable`，实现最终开发者与源码生成工具之间的"对话式协作"。一个源码成员声明可以替换另一个声明——`Replaceable` 类似 `Overridable`，`Replaces` 类似 `Overrides`，`MustReplace` 类似 `MustOverride`（并类比 P/Invoke 的 `Declare` 语句），`NotReplaceable` 类似 `NotOverridable`。这是"带源码生成器的声明式编程"方案。

## Motivation
[motivation]: #motivation

源码生成器目前只能生成"新成员"，难以接管或优化由人书写的算法，也难以让生成的样板代码被人微调。本方案让工具与人在同一个成员上以"声明-替换"的方式协作：

- 人写语义自然的算法，工具为性能优化它（Human-to-Tool）；
- 工具提供默认实现，人只微调需要的部分（Human-to-Tool-to-Human）；
- 人声明需求，工具补全实现、工具再声明自己的需求，人再补全（`MustReplace` 双向协作）。

期望的结果：人与工具在同一源码成员上安全地来回改写，各自只改动自己负责的部分。

## Detailed design
[design]: #detailed-design

### `Replaceable` 与 `Replaces` 修饰符

类比 `Overridable` / `Overrides`：另一个源码成员声明可以替换这一个。

**Human-to-Tool**：人先写语义自然的算法，工具生成性能优化版本。

```vb
' Handwritten.vb
<Optimize>
Replaceable Function ToCommaSeparated(
                       items As IEnumerable(Of Object)
                     )
                     As String

    Let firstItem = items.FirstOrDefault()?
    
    If firstItem Is Null Then Return ""

    Let result = firstItem.ToString()        
        
    For Each item In items Skip 1
        result &= ", " & item.ToString()
    Next
    
    Return result.ToString()
End Function
```

```vb
' ToolGenerated.vb
Replaces Function ToCommaSeparated(
                    items As IEnumerable(Of Object)
                  )
                  As String
                     
    If TypeOf items Is Object() Then
        ' Array fast-path doesn't allocate enumerator
        ' for array and indexing is faster anyway.
        Select Case items.Length
          Case 0
              ' Don't do anything for an empty collection.
              Return String.Empty
          Case 1
              ' For array of 1, simply return its `ToString()`.
              Return items(0).ToString()
          Case Else
              ' Allocate `StringBuilder` as last resort.
              Let builder = New StringBuilder(items.Length * 4)
              
              builder.Append(items(0).ToString())
              
              For i = 1 To builder.Length - 1
                ' Don't concat then append;
                ' append twice.
                  builder.Append(", ")
                  builder.Append(items(i).ToString())
              Next
              
              Return builder.ToString()
        End Select
    Else
        Using enumerator = items.GetEnumerator()
            ' Don't do anything for an empty collection.
            If Not enumerator.MoveNext() Then Return String.Empty

            Let firstItem = enumerator.Current
            
            ' For collection of 1, simply return its `ToString()`.
            If Not enumerator.MoveNext() Then Return firstItem.ToString()

            ' Allocate `StringBuilder` as last resort.
            Let builder = New StringBuilder(firstItem.ToString())
              
            Do
                ' Don't concat then append;
                ' append twice.
                builder.Append(", ")
                builder.Append(enumerator.Current.ToString())
            Loop While enumerator.MoveNext()
              
            Return builder.ToString()
        End Using
    End If
End Function
```

工具版本针对数组走"免枚举器"快路径，针对单个元素直接 `ToString()`，否则用 `StringBuilder` 二次 `Append`。`Replaces` 声明与 `Replaceable` 声明同名同签名，运行时采用工具的版本。

**Human-to-Tool-to-Human**：工具按约定生成默认实现，人只微调需要的部分。注意这里的"对话式往返"。

```vb
' Handwritten.vb - Step 1.
Class Student
    
    Property StudentId As String
    Property LastName As String
    Property FirstName As String
        
End Class

<Xmlifiable>
Class AthleticsTeam
    
    Property Sport As String
    Property School As String
    Property Coach As String
    ReadOnly Property Players As New List(Of Student)
    
End Class
```

```vb
' ToolGenerated.vb - Step 2.
Partial Class AthleticsTeam

    Replaceable Function ToXml() As XElement
        <Team>
          <School>School</>
          <Sport>Sport</>
          <Coach>Coach</>
          <Players>
            For Each student In Players
                Yield ToXml(student)
            Next
          </Players>
        </Team>
    End Function
    
    Replaceable Shared Function ToXml(student As Student) As XElement
        <Student>
          <StudentId>student.StudentId</>
          <LastName>student.LastName</>
          <FirstName>student.FirstName</>
        </Student>
    End Function
    
End Class
```

```vb
' Handwritten.vb - Step 3.
Class Student
    
    Property StudentId As String
    Property LastName As String
    Property FirstName As String
        
End Class

<Xmlifiable>
Class AthleticsTeam
    
    Property Sport As String
    Property School As String
    Property Coach As String
    ReadOnly Property Players As New List(Of Student)

    ' Tweaks the default (generated) implementation of
    ' serializing a student without losing the benefit
    ' of the generated boilerplate logic for the team.
    Replaces Shared Function ToXml(student As Student) As XElement
        <Player id-number={student.StudentId}>
          <FullName>student.LastName & ", " & student.FirstName</>
        </Player>
    End Function
    
End Class
```

人只 `Replaces` 掉序列化单个 `Student` 的 `ToXml`，团队本身的 `ToXml` 仍用工具生成的样板逻辑，因此"微调"不会失去工具生成代码带来的收益。

### `MustReplace` 修饰符

类比 `MustOverride` 与 P/Invoke 的 `Declare` 语句：指定的名字与签名必须由另一个源码成员声明补全。

**Human-to-Tool-to-Human**（双向补全）：开发者先声明需求：

```vb
' Handwritten.vb - Step 1.
<StoredProcedure("Customers_SelectByCity")>
MustReplace Function GetCustomersByCity(city As String) As List(Of String)
```

工具补全这个需求，但同时声明自己的新需求：

```vb
' ToolGenerated.vb - Step 2.
MustReplace Function CreateConnection() As DbConnection

Replaces Function GetCustomersByCity(city As String) As List(Of String)
    Using connection = CreateConnection()
        With command = connection.CreateCommand()
            .CommandType = CommandType.StoredProcedure
            .CommandText = "Customers_SelectByCity"
            
            With .CreateParameter()
                .ParameterName = "@city"
                .DbType = DbType.String
                .Value = city
                
                command.Add(.Me)
            End With
            
            connection.Open()
            Using reader = .ExecuteReader()
                Let results = New List(Of String)
                        
                Do While reader.Read()
                    results.Add(reader.GetString(0))
                Loop
                connection.Close()
                
                Return results
            End Using
        End With
    End Using
End Function
```

人再补全工具声明的 `CreateConnection` 需求：

```vb
' Handwritten.vb - Step 3.
<StoredProcedure("Customers_SelectByCity")>
MustReplace Function GetCustomersByCity(city As String) As List(Of String)

Replaces Function CreateConnection() As DbConnection
    Return New SqlConnection(
                 ConfigurationManager.ConnectionStrings!AcmeDb.ConnectionString
               )
End Function
```

`MustReplace` 允许工具与人双向声明"我需要某成员被补全"，未补全即编译错误，形成编译期保证的协作契约。

### `NotReplaceable` 修饰符

类比 `NotOverridable`：提供的实现不得被替换。只有最终设计包含"某些声明默认可替换"（例如自动实现属性与事件默认可替换）时才有必要：

```vb
' 仅当默认可替换的机制存在时，NotReplaceable 才有意义。
' 示例：NotReplaceable Property Id As Integer
```

## Drawbacks
[drawbacks]: #drawbacks

- 引入四套新修饰符，与继承体系的 `Overridable`/`Overrides`/`MustOverride`/`NotOverridable` 高度平行，语言学习面扩大。
- "替换"边界难界定：签名、可见性、特性、文档注释哪些随替换生效，需要明确规则。
- 与源码生成器的机制深度耦合，工具作者的实现负担大；若工具行为不可预期，协作体验反而变差。
- 生成文件与人写文件的耦合（通过签名匹配）脆弱，重命名/改签名时易产生隐藏的替换失效。

## Alternatives
[alternatives]: #alternatives

- 不引入修饰符，工具通过配置文件/命名约定声明哪些成员可替换（无编译期保证）。
- 只做"只读"方向：工具生成的代码允许人覆盖（Human-to-Tool-to-Human），不做 `MustReplace` 双向契约。
- 依赖 13.1 的智能属性做声明式编程，避免生成器复杂生态。
- 完全依赖 C# 侧已有源码生成器惯例（分部类 + 额外生成文件），不新增语言机制。

## Unresolved questions
[unresolved]: #unresolved-questions

- `Replaces` 替换的成员继承规则（如被替换成员的可见性、访问修饰符是否必须一致）。
- `MustReplace` 允许出现多次 `Replaces` 同名成员时如何判定（是否与 `Overloads` 兼容）。
- `NotReplaceable` 是否必须、默认可替换的声明种类有哪些（Anthony 仅在文字中提及"仅当某些声明默认可替换时有用"）。
- 生成文件与手写文件之间的签名匹配是否需要引入"匹配键"（attribute/key）避免重命名破坏。
