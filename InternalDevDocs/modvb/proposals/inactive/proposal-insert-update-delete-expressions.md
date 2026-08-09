# 插入/更新/删除表达式 / Insert / Update / Delete Expressions

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

新增 `Insert`、`Update`、`Delete` 三种语句式表达式，以声明式写法对数据源（如 EF `DbSet`）执行增删改：`Insert s In db.Students From dto In data Set s.Name = dto!name`、`Update t In db.Teachers Where ... Set ...`、`Delete c In db.Classes Where c.IsDeleted`。

## Motivation
[motivation]: #motivation

- 当前的增删改要么是 ORM 命令式 API（`Add` / `Update` / `Remove`），要么是手写 SQL/查询，样板多且与 LINQ 风格割裂；
- `Insert`/`Update`/`Delete` 表达式把"目标集合 + 匹配条件 + 要设置的字段"集中表达，形似查询表达式，意图一目了然；
- 适合以 DTO 批量灌入数据（如从 JSON 数组 `data` 生成新记录）。

## Detailed design
[design]: #detailed-design

### Insert 表达式

```vb
' Insert expression.
Let data = [
      {"name": "Leo"},
      {"name": "Donny"},
      {"name": "Raph"},
      {"name": "Mikey"}
    ]

Let newStudentIds =
      Insert s In db.Students
        From dto In data
         Set s.Name = dto!name
```

`Insert s In db.Students` 声明插入的目标实体 `s` 与集合 `db.Students`；`From dto In data` 提供来源数据（此处是数组字面量里的 JSON 式 DTO）；`Set s.Name = dto!name` 把 `dto` 的 `name` 字段映射到 `s.Name`。整个表达式的结果 `newStudentIds` 是插入后的新记录 ID 集合。

### Update 表达式

```vb
' Update expression.
? Update
    t In db.Teachers
  Where
    t.Id = teacherId
  Set
    t.EmailAddress = $"{t.FirstName}.{t.LastName}@university.edu"
```

`Update t In db.Teachers` 声明更新目标；`Where t.Id = teacherId` 限定匹配记录；`Set t.EmailAddress = ...` 用插值字符串组合出新邮箱并赋值。

### Delete 表达式

```vb
' Delete expression.
? Delete c In db.Classes Where c.IsDeleted
```

`Delete c In db.Classes Where c.IsDeleted` 删除 `db.Classes` 中满足 `c.IsDeleted` 的记录。`Where` 子句与查询表达式一致。

## Drawbacks
[drawbacks]: #drawbacks

- `Insert`/`Update`/`Delete` 是带副作用的操作，放进表达式语法会打破"表达式无副作用"的直觉，需要明确求值时机与执行模型。
- 更新/删除的多记录语义（是否批量、是否逐条）需与具体数据源（EF/ADO/SQL）绑定，语言层难以统一。
- 与 `Delete` 关键字在 `File.Delete`、集合 `Remove` 等既有用法的视觉冲突。

## Alternatives
[alternatives]: #alternatives

- 维持 ORM 命令式 API（`DbSet.Add`/`Remove`/`SaveChanges`）不动，不新增语句式表达式；
- 只提供查询式 `Insert`（批量插入），`Update`/`Delete` 继续走命令式；
- 用扩展方法/表达式树模拟（如 `db.Students.Delete(...)`），但可读性差、类型安全弱。

## Unresolved questions
[unresolved]: #unresolved-questions

- `Insert` 表达式如何返回新记录的 ID（是否默认返回 `IEnumerable(Of Key)`，还是可配置）。
- `Update`/`Delete` 的返回值语义：受影响行数、匹配行数还是记录集合。
- `Set` 子句是否支持复合赋值、是否允许调用方法（副作用）。
- 是否要求数据源是 EF `DbSet` 专用，还是对任意可查询对象均适用；`dto!name` 字典/动态访问的绑定规则。
- 事务与批量执行的编排（多条 `Insert`/`Update`/`Delete` 是否在同一事务中提交）。
