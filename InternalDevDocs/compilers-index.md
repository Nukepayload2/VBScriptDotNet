# Compilers 索引（VB + C# 编译器）

## 用途

为仓库根 `Compilers\` 目录编制一份「目录结构 + 关键词」索引。目标是：后续 VBScript.NET 实现与设计时，只读这份索引就能快速定位「某个功能/概念在编译器树的哪个目录」，再按需深入读源码，省去逐个目录摸索。

## 如何使用

- 从**一、方向摘要**读大图：了解编译器管线的分层与各阶段职责。
- 从**二、目录结构与关键词索引**定位：按切片（Core 公共层 / C# / VB / 驱动测试）查「目录 → 关键词」，关键词即 Grep 定位用词。
- 从**三、与 VBScript.NET 的关系**看实现入口建议。
- 本索引只记**目录与关键词**，不记具体文件，降低维护成本；源码结构变化时只需更新目录层，不需逐文件校对。

## 来源目录

- 根：`Compilers\`（本索引全部相对路径以此为根）
- 结构：`Core\`（语言无关核心）+ `CSharp\`（C# 编译器）+ `VisualBasic\`（VB 编译器）+ `Shared\`（共享驱动/构建服务器）+ `Test\`（测试基础设施与资源）+ 7 个 `VisualBasic*Test\` 项目 + `CSharp\Test\`
- 三大编译器工程各为 `Portable\` 子目录：`Microsoft.CodeAnalysis`（Core）、`Microsoft.CodeAnalysis.CSharp`、`Microsoft.CodeAnalysis.VisualBasic`

## 版本快照

- 语言版本：C# **CSharp14**（`LanguageVersion.CurrentVersion`）；VB **VisualBasic17_13**（`Latest`）。
- 目标框架：`netstandard2.0;net10.0`。
- 来源：修剪/本地化的 Roslyn 编译器树，非完整上游镜像。
- 上游基准：`{{Roslyn}}` 基准 commit 见 `upstream-merge.md`（一、上游基准）。

## VBScript 专项提示（本树已有修改）

- **`.vbx` 脚本**：VB 命令行的 `ScriptFileExtension` 已改为 `.vbx`，`SourceCodeKind.Script` 支撑 `.vbx` 与交互式提交（`.csx`/`.vbx`）。
- **`vbi` 交互模式**：`\i` 选项或无参启动进入交互式；`vbi script.vbx` 直接执行脚本。
- **脚本提交语义**：`.vbx` 提交代码中 `Return` 按 `Function Main` 语义处理（typed submission → vbx exit code）。
- 定位：见切片 VB-BIND 的 `CommandLine` 行与 CORE-BASE 的 `Compilation`/根层行。

## 一、方向摘要 T1–T10

**T1 三层编译器布局**：`Core`（语言无关）+ `CSharp` + `VisualBasic`。三者共享 Syntax/Text/Binding/Compilation/Diagnostic/Emit/PEWriter/Symbols 等基础设施；C# 与 VB 各自提供语言特有的解析、绑定、降级、符号与发射。

**T2 语法层（Green/Red 双树）**：公共 `Syntax` 目录实现不可变 GreenNode 底层树 + 公共 SyntaxNode 红树包装，Token/Trivia/List/Walker/导航/差异/行映射齐全。C# 与 VB 各自的 `Syntax` 目录提供语言语法节点层次、SyntaxKind、SyntaxFactory、Visitor/Walker/Rewriter，其下 `InternalSyntax` 为绿色内部树。VB 另有独立 `Scanner`（词法：Token/关键字表/XML 词法/增量重扫 Blender）与 `Preprocessor`（#Const/#If 求值）。

**T3 解析与声明合并**：各语言 `Parser` 目录为递归下降解析器（C# 含 Lexer/LanguageParser/Blender 增量；VB 含 ParserFeature 特性门控、BlockContext、ParseVerify 语句终止判定）。`Declarations` 目录做跨文件命名空间/类型声明合并表。

**T4 绑定与语义**：`Binder`/`Binding` 目录为符号绑定核心（名字→符号、语法→BoundTree、作用域 Binder 链、查找/重载/晚期绑定/OptionStrict）。`BoundTree` 为绑定产物语义树（BoundNode + Visitor/Walker/Rewriter）。`Semantics`/`Binder/Semantics` 为转换、运算符重载决议、方法组/类型推断。C# 侧另有 `FlowAnalysis`（数据流/确定赋值/Nullable），VB 侧 `Analysis`（方法体流分析/For 校验）。

**T5 编译对象与语义模型**：`Compilation` 目录为不可变 Compilation/Options/SemanticModel/编译阶段/脚本提交信息（ScriptCompilationInfo 支撑 .vbx）。`Errors` 目录为错误码/诊断（C# ERR_*，VB BC 前缀）/消息格式化/惰性诊断。

**T6 降级管线（Lowering）**：各语言 `Lowering` 目录把已绑定树降级到可发射 IL。C#：ClosureConversion（闭包/显示类）、StateMachineRewriter 基类、AsyncRewriter、IteratorRewriter、LocalRewriter（70+ 文件按语法构造化简）、Instrumentation。VB：LocalRewriter（ReDim/With/On Error/晚绑定/Select Case 等 VB 特有语义归一化）、LambdaRewriter、AsyncRewriter、IteratorRewriter、MethodToClassRewriter 底座、ExpressionLambdaRewriter、Instrumentation、Diagnostics。

**T7 代码生成与发射**：`CodeGen` 目录从 Bound/降级树生成 IL（CodeGenerator、ILBuilder、基本块、局部变量槽、序列点）。`Emit`（Core）/`Emitter`（C#）目录构建 PE 模块顶层模型（CommonPEModuleBuilder/PEModuleBuilder/PEAssemblyBuilder、EmitOptions），含 EnC/EditAndContinue 与 NoPia 子层。`PEWriter` 写 PE 文件与 ECMA-335 元数据（PeWriter/MetadataWriter）。`NativePdbWriter` 写原生 Windows PDB。`DiaSymReader` 为 PDB COM 互操作。

**T8 符号系统**：`Symbols` 目录为符号对象模型（ISymbol 系列 + 各语言实现）。子层：`Source`（源码声明符号）、`Metadata/PE`（元数据导入符号 + MetadataDecoder）、`Synthesized`/`SynthesizedSymbols`（合成符号 + GeneratedNames 命名编码）、`Retargeting`、`AnonymousTypes`、`Attributes`、`Extensions`（C#）、`FunctionPointers`（C#）、`PublicModel`（C#，内部→公开 ISymbol 适配）、`EmbeddedSymbols`（VB）、`Wrapped`（VB）、`Tuples`（VB）。`SymbolDisplay` 为符号→字符串格式化（错误消息/智能提示）。公共侧 `MetadataReader`/`MetadataReference`/`ReferenceManager` 为元数据读取与程序集引用解析。

**T9 诊断与代码分析**：Core `Diagnostic` 为诊断/位置/本地化/抑制模型；`DiagnosticAnalyzer` 为分析器框架（AnalyzerDriver、CompilationWithAnalyzers、Suppression）；`Operations` 为 IOperation 语义操作树 + ControlFlowGraph（供分析器/IDE/修复）；`SourceGeneration` 为源生成器驱动（GeneratorDriver/ISourceGenerator/IIncrementalGenerator）；`CommandLine` 为语言无关命令行解析（CommandLineParser/CommonCompiler/SARIF 错误日志）。

**T10 命令行驱动与测试**：`csc`/`vbc` 为命令行入口（Program→BuildClient→Csc/Vbc.Run，rsp 响应文件）；`Shared` 为构建服务器客户端（BuildClient/BuildServerConnection/协议）+ GAC 解析。`Test\Core` 为跨语言测试基础设施（TestBase/CommonTestBase/CompileAndVerify/TempRoot/MarkupTestFile/Platform 运行时封装/资源），`Test\Utilities\VisualBasic` 为 VB 测试基类（BasicTestBase）。7 个 `VisualBasic*Test` 项目按维度拆测试（CommandLine/Emit/IOperation/Semantic/Symbol/Syntax/综合），`CSharp\Test` 同构。

## 二、目录结构与关键词索引

> 全部相对路径以 `Compilers\` 为根。表格「关键词」列是 Grep 定位用词；「相关性」列评估对 VBScript.NET 的价值。**本索引不引用具体文件**，只到目录层。

### CORE-BASE（语言无关核心基础层）

```
Core\Portable\
├── Syntax/          —— Green/Red 双树、节点/标记/琐碎、遍历与定位
├── Text/            —— SourceText 不可变文本、行/位置/跨度、变化与哈希
├── Binding/         —— 符号绑定期辅助（UseSiteInfo、绑定诊断袋）
├── Compilation/     —— Compilation/SemanticModel、编译阶段、脚本提交信息
├── Diagnostic/      —— 诊断/位置/本地化/抑制模型
├── Collections/     —— 内部高性能专用集合
├── InternalUtilities/ —— 通用工具（StringTable/Unicode 字符分类/并发/枚举）
├── Hashing/         —— xxHash 非加密哈希（内容哈希/确定性）
├── Resources/ Xml/ FileSystem/ —— 资源、XML 字符分类、文件路径解析
├── Generated/       —— 自动生成代码（IOperation/FlowAnalysis 基元）
└── （根）           —— 语言无关公共枚举/特殊类型/WellKnown 元数据表
```

| 目录 | 一句话职责 | 关键词 | 相关性 |
|---|---|---|---|
| Syntax | 语法树基础设施：GreenNode 不可变底层树 + SyntaxNode 红树包装 | SyntaxTree, SyntaxNode, SyntaxToken, SyntaxTrivia, GreenNode, SyntaxList, SeparatedSyntaxList, SyntaxWalker, SyntaxNavigator, SyntaxReference, SyntaxDiffer, LineDirectiveMap, IStructuredTriviaSyntax | 高——VBScript 解析器/语法树必须复用这套双树与遍历框架 |
| Text | 源码文本抽象与编辑 | SourceText, SourceTextContainer, TextSpan, TextLine, LinePosition, TextChangeRange, SourceHashAlgorithm | 高——解析源文本、行号映射、增量变更落点 |
| Binding | 绑定期辅助类型 | UseSiteInfo, CompoundUseSiteInfo, BindingDiagnosticBag, AbstractLookupSymbolsInfo | 中——VBScript 绑定阶段复用依赖追踪与诊断袋 |
| Compilation | 编译核心与编译阶段、脚本提交 | Compilation, CompilationOptions, ParseOptions, CompilationStage, SemanticModel, ScriptCompilationInfo, SymbolInfo, TypeInfo, DataFlowAnalysis, EmitResult, OutputKind, OptimizationLevel | 高——编译入口；ScriptCompilationInfo 支撑 .vbx 提交（PreviousScriptCompilation/ReturnType/GlobalsType） |
| Diagnostic | 诊断与位置模型 | Diagnostic, DiagnosticDescriptor, DiagnosticSeverity, DiagnosticBag, Location, FileLinePositionSpan, CommonMessageProvider, SuppressionDescriptor, LocalizableString | 高——VBScript 诊断/错误报告/抑制完全复用 |
| Collections | 编译器内部高性能集合 | ArrayBuilder, BitVector, SmallDictionary, OrderedSet, TopologicalSort, ImmutableMemoryStream, MultiDictionary | 中——绑定/语义分析对小字典、有序集合依赖强 |
| InternalUtilities | 内部通用工具 | StringTable, UnicodeCharacterUtilities, RoslynString, ThreeState, MultiDictionary, ConsList, JsonWriter, FileNameUtilities | 中——Unicode 字符分类、字符串驻留是词法/解析必需 |
| Hashing | 非加密哈希 | XxHash128, NonCryptographicHashAlgorithm | 低-中——SourceText 内容哈希/确定性编译 |
| Resources | 资源与清单 | default.win32manifest, CodeAnalysisResources | 低 |
| Xml | XML 字符分类 | XmlCharType | 低 |
| FileSystem | 文件/路径解析抽象 | PathUtilities, FileUtilities, RelativePathResolver, ICommonCompilerFileSystem | 中——命令行/引用解析依赖 |
| Generated | 自动生成代码 | Operations.Generated, OperationKind.Generated, FlowAnalysis.Generated | 中——若实现 IOperation 语义树则复用 |
| （根） | 语言无关公共类型与元数据表 | SourceCodeKind(Regular/Script/Interactive), OutputKind, SpecialType, WellKnownMember, WellKnownTypes, ConstantValue, DocumentationMode, EmbeddedText, SignatureComparer, CaseInsensitiveComparison | 高——SourceCodeKind.Script 文档明确标注用于 .csx/.vbx；SpecialType/WellKnownMember 是跨语言共享元数据表 |

### CORE-SYMBOLS-META（符号 API 与元数据读取）

```
Core\Portable\
├── Symbols/          —— 符号 API 抽象（ISymbol 系列 + 元数据）
│   ├── AnonymousTypes/  —— 匿名类型公共管理
│   └── Attributes/      —— 属性解码与 well-known attribute 数据
├── SymbolDisplay/    —— 符号到字符串的格式化显示
├── DocumentationComments/ —— XML 文档注释解析
├── MetadataReader/   —— 元数据读取/解码（PE 模块、ECMA-335 签名）
├── MetadataReference/ —— 元数据引用抽象（程序集身份/引用）
├── ReferenceManager/ —— 程序集引用绑定与解析
├── Interop/ Desktop/ StrongName/ —— CLR 宿主/桌面身份比较/强名称
├── DiaSymReader/     —— PDB 符号读写（COM 互操作）
└── RuleSet/          —— ruleset 分析规则文件解析
```

| 目录 | 一句话职责 | 关键词 | 相关性 |
|---|---|---|---|
| Symbols | 符号对象模型（公共 API 面） | ISymbol, IAssemblySymbol, INamedTypeSymbol, INamespaceSymbol, ITypeSymbol, IMethodSymbol, IFieldSymbol, IEventSymbol, IPropertySymbol, IParameterSymbol, ITypeParameterSymbol, IArrayTypeSymbol, IPointerTypeSymbol, IErrorTypeSymbol, ILocalSymbol, SymbolKind, TypeKind, MethodKind, Accessibility, RefKind, NullabilityInfo, SymbolVisitor, SymbolEqualityComparer, WellKnownMemberNames, CustomModifier, TypedConstant | 高——任何 VBScript 语言绑定都建立在 ISymbol 模型上 |
| Symbols/AnonymousTypes | 匿名类型符号管理 | CommonAnonymousTypeManager, AnonymousType | 中 |
| Symbols/Attributes | 属性解码与 well-known 属性元数据 | AttributeData, CommonAttributeData, WellKnownAttributeData, ObsoleteAttributeData, AttributeUsageInfo, CustomAttributesBag | 高——读引用程序集与源码特性都经此层 |
| SymbolDisplay | 符号格式化显示 | SymbolDisplayFormat, SymbolDisplayPart, AbstractSymbolDisplayVisitor, FormattedSymbol, SymbolDisplayMemberOptions | 高——VBScript 错误诊断文本/补全/悬停展示依赖 |
| DocumentationComments | XML 文档注释解析 | DocumentationProvider, XmlDocumentationCommentTextReader | 中 |
| MetadataReader | 元数据导入（核心） | PEModule, PEAssembly, MetadataDecoder, MetadataTypeName, MetadataImportOptions, SymbolFactory | 高——VBScript 读取 mscorlib/COM 互操作程序集类型全走这里 |
| MetadataReference | 元数据引用抽象与程序集身份 | MetadataReference, AssemblyMetadata, ModuleMetadata, AssemblyIdentity, PortableExecutableReference, CompilationReference, MetadataReferenceResolver | 高——VBScript 引用解析的核心 |
| ReferenceManager | 程序集引用绑定/别名/合并 | CommonReferenceManager, AssemblyData, AssemblyReferenceBinding, BoundInputAssembly, UnifiedAssembly, MergedAliases | 中高——版本绑定/别名解析 |
| Interop | CLR 宿主 COM 互操作 | IClrStrongName, IClrMetaHost, ComImport | 低 |
| Desktop | 桌面框架身份比较 | DesktopAssemblyIdentityComparer, AssemblyPortabilityPolicy | 中低 |
| StrongName | 程序集强名称签名 | StrongNameProvider, StrongNameKeys, CryptoBlobParser | 低 |
| DiaSymReader | PDB 调试符号读写 | SymUnmanagedWriter, ISymUnmanagedWriter, SymUnmanagedSequencePointsWriter | 中——需调试（序列化点/文档表）才涉及 |
| RuleSet | ruleset 分析规则文件解析 | RuleSet, RuleSetInclude, RuleSetProcessor | 低 |

### CORE-EMIT-DIAG（代码生成、诊断与分析）

```
Core\Portable\
├── CodeGen/           —— IL 生成（ILBuilder、基本块、方法体、局部变量）
├── Emit/              —— 编译单元→PE 模块顶层构建（CommonPEModuleBuilder、EmitOptions）
│   ├── EditAndContinue/ —— EnC 增量发射
│   └── NoPia/           —— 嵌入互操作类型
├── PEWriter/          —— PE 文件与 ECMA-335 元数据写入（PeWriter、MetadataWriter）
├── NativePdbWriter/   —— 原生 Windows PDB 写入
├── Operations/        —— IOperation 语义操作树 + 控制流图 CFG
│   └── Loops/         —— 循环操作信息（ForEach/ForTo/LoopKind）
├── SourceGeneration/  —— 源生成器驱动（GeneratorDriver）
│   └── Nodes/         —— 增量生成器节点图
├── DiagnosticAnalyzer/ —— 分析器框架与执行引擎
└── CommandLine/       —— 语言无关命令行解析与编译器驱动
Core\AnalyzerDriver\   —— 共享 analyzer 声明计算辅助（.shproj）
```

| 目录 | 一句话职责 | 关键词 | 相关性 |
|---|---|---|---|
| CodeGen | 方法体 IL 生成 | ILBuilder, BasicBlock, EmitState, LabelInfo, LocalSlotManager, LocalDefinition, SynthesizedLocalKind, MethodBody, SequencePointList, SwitchJumpTableEmitter, MetadataConstant | 高——VBScript 把语句/表达式翻译成 IL 即落点 |
| Emit | 编译单元→PE 模块构建 | CommonPEModuleBuilder, EmitOptions, EmitContext, DebugDocumentsBuilder, DebugInformationFormat, InstrumentationKind, SemanticEdit, Deterministic | 高——语言无关模块构建层，发射必经 |
| Emit/EditAndContinue | EnC 增量发射 | DeltaMetadataWriter, EmitBaseline, SymbolMatcher, SymbolChanges, DefinitionMap, EncHoistedLocalInfo | 中——热重载/EnC 调试才相关 |
| Emit/NoPia | 嵌入互操作类型 | EmbeddedTypesManager, CommonEmbeddedType, VtblGap | 低 |
| PEWriter | PE 文件与元数据写出 | PeWriter, MetadataWriter, ExtendedPEBuilder, PooledBlobBuilder, ReferenceIndexer, CustomDebugInfoWriter, ExportedType, Deterministic | 高——定制元数据/强命名/模块特性时直接改这里 |
| NativePdbWriter | 原生 Windows PDB | PdbWriter, ISymWriterMetadataProvider | 低 |
| Operations | 语义操作树 IOperation + CFG | IOperation, OperationKind, OperationFactory, OperationVisitor, ControlFlowGraph, ControlFlowGraphBuilder, BasicBlockKind, ControlFlowBranch, ControlFlowRegion | 中——供语义分析/分析器/代码修复用 |
| SourceGeneration | 源生成器驱动与增量管道 | GeneratorDriver, ISourceGenerator, IIncrementalGenerator, IncrementalValueProvider, GeneratorSyntaxWalker, WellKnownGeneratorInputs | 中——暴露源生成器 SDK 时相关 |
| DiagnosticAnalyzer | 诊断分析器框架 | DiagnosticAnalyzer, AnalyzerDriver, AnalyzerManager, CompilationWithAnalyzers, AnalyzerOptions, DiagnosticSuppressor, DiagnosticDescriptor, AnalyzerReference | 高——VBScript 编译诊断/静态分析复用此框架 |
| CommandLine | 命令行解析与语言无关驱动 | CommandLineParser, CommandLineArguments, CommonCompiler, AnalyzerConfig, ErrorLogger, SarifErrorLogger, BuildPaths | 高——VBScript 命令行外壳（仿 vbsc）的语言无关基础 |
| （共享）AnalyzerDriver | analyzer 声明计算辅助 | DeclarationComputer, DeclarationInfo | 中 |

### CSHARP-BIND（C# 语法/解析/绑定侧）

```
CSharp\Portable\
├── （根）           —— 语言版本、编译/解析选项、扩展方法
├── Syntax/          —— 红色语法树：节点、SyntaxKind、工厂、访问器
│   └── InternalSyntax/ —— 绿色内部树
├── Parser/          —— 词法(Lexer)+语法(LanguageParser)+预处理/文档注释/增量 Blender
├── BoundTree/       —— 绑定后语义树（BoundNode + Visitor/Walker/Rewriter）
├── Binder/          —— 符号绑定核心 + 作用域 Binder
│   └── Semantics/   —— Conversions/Operators/OverloadResolution
├── Declarations/    —— 跨文件声明合并表
├── Compilation/     —— CSharpCompilation + 语义模型
├── Compiler/        —— IL 方法体编译
├── Errors/          —— 诊断/错误码枚举/惰性诊断
├── FlowAnalysis/    —— 数据流/控制流/确定赋值/Nullable
├── Operations/      —— BoundNode → IOperation 工厂
├── Utilities/       —— 模式匹配值集合（ValueSet）
└── CommandLine/     —— csc 命令行解析与驱动
```

| 目录 | 一句话职责 | 关键词 | 相关性 |
|---|---|---|---|
| （根） | 语言入口：版本/选项 | LanguageVersion(CSharp14), LanguageVersionFacts, CSharpCompilationOptions, CSharpParseOptions, CSharpExtensions, SpecifiedLanguageVersion | 中——等价编译/解析选项入口 |
| Syntax | C# 语法树公开 API | CSharpSyntaxNode, CSharpSyntaxTree, SyntaxKind, SyntaxFacts, SyntaxFactory, CSharpSyntaxVisitor/Walker/Rewriter, CompilationUnitSyntax, *Declaration/Expression/StatementSyntax | 高——VBScript 需自建语法树层次与工厂 |
| Syntax/InternalSyntax | 绿色内部树 | GreenNode, InternalSyntax.CSharpSyntaxNode, SyntaxToken, SyntaxTrivia | 中——绿/红树模式可借鉴 |
| Parser | 文本→语法树 | LanguageParser, Lexer, SlidingTextWindow, DirectiveParser, DocumentationCommentParser, Blender, QuickScanner, LexerCache | 高——VBScript 全新解析器的架构蓝本 |
| BoundTree | 绑定产物语义树 | BoundNode, BoundKind, BoundExpression, BoundStatement, BoundTreeVisitor/Walker/Rewriter, BoundCall, BoundConversion, BoundBinaryOperator, BoundPattern, BoundDecisionDag, UnboundLambda | 高——VBScript 绑定阶段需自身 Bound 节点层次 |
| Binder | 符号绑定核心 | Binder, BinderFlags, BinderFactory, BlockBinder, LocalScopeBinder, SwitchBinder, CatchClauseBinder, InMethodBinder, ScriptLocalScopeBinder, LookupResult, MethodGroupResolution, BindingDiagnosticBag | 高——搜"C# 绑定"即此目录 |
| Binder/Semantics | 转换/运算符/重载决议 | Conversions, TypeConversions, UserDefinedConversions, ConversionKind, OperatorKind, BinaryOperatorOverloadResolution, OverloadResolution, MethodTypeInference, MethodGroup, MemberResolutionResult | 高——VBScript 需类型转换、运算符与重载决议 |
| Declarations | 跨文件声明合并 | DeclarationTable, DeclarationKind, DeclarationModifiers, MergedNamespaceDeclaration, SingleTypeDeclaration | 中 |
| Compilation | 编译对象与语义模型 | CSharpCompilation, CSharpSemanticModel, SyntaxTreeSemanticModel, MemberSemanticModel, SpeculativeSemanticModel, SyntaxAndDeclarationManager | 高——VBScript 编译对象与语义模型对应物 |
| Compiler | IL 方法体编译 | MethodCompiler, MethodBodySynthesizer, SynthesizedMetadataCompiler, TypeCompilationState | 中 |
| Errors | 诊断与错误码 | ErrorCode, ERR_*/WRN_*, MessageProvider, CSDiagnostic, LazyDiagnosticInfo, ErrorFacts | 中——VBScript 需自身错误码/消息提供者 |
| FlowAnalysis | 数据流/确定赋值/Nullable | AbstractFlowPass, ControlFlowPass, LocalDataFlowPass, DefiniteAssignment, NullableWalker, CSharpDataFlowAnalysis | 高——未初始化变量/确定赋值检查蓝本 |
| Operations | BoundNode→IOperation | CSharpOperationFactory, IOperation | 中 |
| Utilities | 模式匹配值集合 | ValueSetFactory, IValueSet, NumericValueSet, TypeUnionValueSet | 中 |
| CommandLine | csc 命令行 | CSharpCommandLineParser, CSharpCommandLineArguments, CSharpCompiler | 中 |
| Generated | 自动生成代码 | BoundNodes.xml.Generated, ErrorFacts.Generated | 低——Bound 节点由生成器产出，改节点须改 .xml 源 |

### CSHARP-EMIT-SYMBOLS（C# 符号/降级/发射侧）

```
CSharp\Portable\
├── Symbols/          —— 符号体系（最庞大）
│   ├── Source/       —— 源码声明符号
│   ├── Metadata/PE/  —— 元数据导入符号
│   ├── Synthesized/  —— 合成符号 + GeneratedNames
│   ├── Retargeting/  —— 引用版本重定向包装
│   ├── AnonymousTypes/ —— 匿名类型管理
│   ├── Attributes/   —— 特性数据模型
│   ├── Extensions/   —— 扩展方法实现/重写符号
│   ├── FunctionPointers/ —— 函数指针类型符号
│   └── PublicModel/  —— ISymbol 公开 API 适配层
├── SymbolDisplay/    —— 符号格式化显示
├── Lowering/         —— Bound 树降级/重写管线
│   ├── ClosureConversion/ —— lambda/局部函数闭包（LambdaRewriter 实质）
│   ├── StateMachineRewriter/ —— 状态机重写基类
│   ├── AsyncRewriter/ —— async 状态机重写
│   ├── IteratorRewriter/ —— 迭代器状态机重写
│   ├── LocalRewriter/ —— 表达式/语句级化简（70+ 文件）
│   └── Instrumentation/ —— 代码插桩
├── CodeGen/          —— IL 指令发射（CodeGenerator + Optimizer）
├── Emitter/          —— 程序集/元数据发射
│   ├── Model/        —— 发射对象模型（PEModuleBuilder）
│   ├── EditAndContinue/ —— EnC 增量发射
│   └── NoPia/        —— 嵌入互操作类型
└── SourceGeneration/ —— 源生成器驱动
```

| 目录 | 一句话职责 | 关键词 | 相关性 |
|---|---|---|---|
| Symbols（顶层） | 符号抽象基类与通用符号 | Symbol, TypeSymbol, NamedTypeSymbol, MethodSymbol, FieldSymbol, PropertySymbol, ParameterSymbol, LocalSymbol, AssemblySymbol, NamespaceSymbol, ConstructedNamedTypeSymbol, ErrorTypeSymbol, TypeMap, TypeWithAnnotations | 高——符号体系是语言编译器公共地基 |
| Symbols/Source | 源码声明符号 | SourceMethodSymbol, SourceNamedTypeSymbol, SourceFieldSymbol, SourcePropertySymbol, SourceLocalSymbol, LambdaSymbol, LocalFunctionSymbol | 高——VBScript 声明都映射到此类符号 |
| Symbols/Metadata/PE | PE 元数据导入符号 | PEAssemblySymbol, PEModuleSymbol, PENamedTypeSymbol, PEMethodSymbol, MetadataDecoder, SymbolFactory, TupleTypeDecoder | 高——引用 .NET 程序集必须复用 |
| Symbols/Synthesized | 合成符号与命名规则 | SynthesizedLocal, GeneratedNameKind, GeneratedNames, SynthesizedMethodSymbol, SynthesizedFieldSymbol, SynthesizedEntryPointSymbol | 高——降级/代码生成必然产生合成符号，命名编码需语言自定义 |
| Symbols/Retargeting | 引用版本重映射 | RetargetingAssemblySymbol, RetargetingNamedTypeSymbol, RetargetingMethodSymbol, RetargetingSymbolTranslator | 中 |
| Symbols/AnonymousTypes | 匿名类型符号 | AnonymousTypeManager, AnonymousTypeDescriptor, AnonymousTypeField | 中 |
| Symbols/Attributes | 特性数据模型 | CSharpAttributeData, SourceAttributeData, PEAttributeData, WellKnownAttributeData | 高——特性处理各语言通用 |
| Symbols/Extensions | 扩展方法实现/重写 | RewrittenMethodSymbol, RewrittenLambdaOrLocalFunctionSymbol, ReceiverParameterSymbol | 中——VBScript 设计扩展方法时需要 |
| Symbols/FunctionPointers | 函数指针类型 | FunctionPointerTypeSymbol | 低 |
| Symbols/PublicModel | 内部→公开 ISymbol 适配 | PublicModel.Symbol, SymbolAdapter | 高——公开 API/IDE 工具链集成必需 |
| SymbolDisplay | 符号→字符串 | SymbolDisplay, SymbolDisplayFormat, AbstractSymbolDisplayVisitor, ObjectDisplay | 高 |
| Lowering（顶层） | 降级管线框架 | SyntheticBoundNodeFactory, MethodToClassRewriter, InitializerRewriter, SpillSequenceSpiller, SynthesizedSubmissionFields | 高——Bound 树降级框架是后端核心 |
| Lowering/ClosureConversion | lambda/局部函数→闭包类 | SynthesizedClosureEnvironment, LambdaCapturedVariable, ClosureKind, ExpressionLambdaRewriter | 中——支持 lambda/闭包才需要 |
| Lowering/StateMachineRewriter | 状态机重写基类 | StateMachineRewriter, MethodToStateMachineRewriter, StateMachineTypeSymbol, StateMachineFieldSymbol, CapturedSymbol | 中——支持 async/iterator 才需要 |
| Lowering/AsyncRewriter | async→状态机 | AsyncRewriter, AsyncStateMachine, AsyncMethodBuilderMemberCollection, AsyncIteratorInfo | 中——VBScript 是否引入 Async/Await 决定 |
| Lowering/IteratorRewriter | 迭代器/yield→状态机 | IteratorRewriter, IteratorStateMachine, IteratorFinallyMethodSymbol | 中 |
| Lowering/LocalRewriter | 表达式/语句级化简（规模最大） | LocalRewriter, BoundTreeRewriterWithStackGuard, DelegateCacheRewriter, DynamicSiteContainer, PatternLocalRewriter | 高——化简逻辑语言无关，VBScript 后端必须复用 |
| Lowering/Instrumentation | 代码插桩 | Instrumenter, CodeCoverageInstrumenter, DebugInfoInjector, StackOverflowProbingInstrumenter | 中 |
| CodeGen | Bound→IL 指令发射 | CodeGenerator, ILBuilder, EmitExpression, EmitStatement, EmitAddress, EmitOperators, EmitConversion, Optimizer | 高——IL 发射核心，语言无关，直接复用 |
| Emitter/Model | 程序集/元数据发射模型 | PEModuleBuilder, PEAssemblyBuilder, MethodReference, NamedTypeReference, SymbolAdapter, AttributeDataAdapter | 高——元数据发射模型全语言通用 |
| Emitter/EditAndContinue | EnC 增量发射 | PEDeltaAssemblyBuilder, CSharpSymbolMatcher, CSharpDefinitionMap | 低 |
| Emitter/NoPia | 嵌入互操作类型 | EmbeddedTypesManager, EmbeddedType | 低 |
| SourceGeneration | 源生成器驱动 | CSharpGeneratorDriver, ISourceGenerator, IIncrementalGenerator, CSharpSyntaxHelper | 中 |

### VB-BIND（VB 语法/解析/绑定侧）

```
VisualBasic\Portable\
├── （根）           —— 语言版本、OptionStrict、GlobalImport、解析/编译选项
├── Syntax/          —— 红色语法树：SyntaxKind、节点、工厂
├── Parser/          —— 递归下降解析器（VB 语法→内部树，含特性开关）
├── Scanner/         —— 词法扫描器：Token/关键字表/指令/XML/增量重扫
├── Preprocessor/    —— #Const/#If 预处理条件表达式求值
├── BoundTree/       —— 绑定后绿色语义树
├── Binding/         —— Binder 链：查找/重载/晚期绑定/OptionStrict
├── Semantics/       —— 运算符表、重载解析、类型转换、编译期求值
├── Declarations/    —— 源文件声明树
├── Compilation/     —— VisualBasicCompilation、SemanticModel、脚本编译信息
├── Errors/          —— 诊断/错误码（BC 前缀）
├── Locations/       —— 位置抽象
├── Operations/      —— BoundNode → IOperation 工厂
├── Utilities/       —— 类型统一、变体歧义
├── Generated/       —— 生成代码 + ANTLR 文法（g4）
├── DocumentationComments/ —— XML 文档注释 ID 生成
├── Analysis/        —— 方法体流分析、For 循环验证、初始化器重写
└── CommandLine/     —— 命令行解析、.vbx 脚本、vbi 交互模式、编译器入口
```

| 目录 | 一句话职责 | 关键词 | 相关性 |
|---|---|---|---|
| （根） | 语言版本/选项/全局导入 | LanguageVersion(VisualBasic17_13), VisualBasicParseOptions(SourceCodeKind), VisualBasicCompilationOptions(OptionStrict/OptionInfer/OptionExplicit/OptionCompareText/RootNamespace/GlobalImports/EmbedVbCoreRuntime/scriptClassName), OptionStrict, GlobalImport, PredefinedPreprocessorSymbols | 高——SourceCodeKind.Script 与 preprocessorSymbols 直接决定 vbx 脚本解析模式 |
| Syntax | 语法树节点/工厂 | SyntaxKind, SyntaxFacts, SyntaxFactory, VisualBasicSyntaxTree, VisualBasicSyntaxVisitor/Walker/Rewriter, ArgumentSyntax, TypeBlockSyntax, MethodBlockBaseSyntax, CompilationUnitSyntax | 高——搜"VB 语法"入口；新语句/表达式需新增 SyntaxKind，关键字在 SyntaxFacts |
| Parser | 递归下降解析器 | Parser, ParseExpression, ParseStatement, ParseVerify, ParseConditional, ParseInterpolatedString, ParseQuery, ParseXml, ParserFeature, ContextAwareSyntaxFactory, BlockContext | 高——搜"VB 解析"定位点；VBScript 语法兼容/裁剪在此加分支 |
| Scanner | 词法扫描 | Scanner, Blender, TokenStream, ScannerState(VB/XML 状态机), Directives, KeywordTable, ScannerInterpolatedString, ScannerXml | 高——词法层；新关键字/字符串字面量规则改 KeywordTable/Scanner |
| Preprocessor | 条件编译求值 | CConst, ExpressionEvaluator, OperatorResolution | 中——VBScript 无预处理，但其常量模型可参考 #Const |
| BoundTree | 绑定后语义树 | BoundNode, BoundKind, BoundExpression, BoundTreeVisitor/Walker/Rewriter, BoundLateInvocation, BoundLateMemberAccess, BoundWithStatement, BoundOnErrorStatement, BoundResumeStatement, BoundDimStatement, BoundRedimClause, BoundAssignmentOperator | 高——On Error Resume Next/Set/ReDim/晚期绑定直接对应这些 Bound 节点，是代码生成基础 |
| Binding | Binder 链 | Binder, BinderBuilder, BinderFactory, ExecutableCodeBinder, MethodBodyBinder, SourceFileBinder, BackstopBinder, Binder_Latebound, Binder_Statements, Binder_Expressions, Binder_Symbols, OptionStrictOffBinder, SpeculativeBinder, SpeculativeSemanticModel, LookupOptions, LookupResult | 高——Binder_Latebound/OptionStrictOffBinder 贴合 VBScript 晚期绑定/宽松语义；WithEvents/Handles 绑定在此 |
| Semantics | 运算符/重载/转换/编译期求值 | OverloadResolution, Operators, Conversions, CType, CompileTimeCalculations, AccessCheck | 中高——VBScript 依赖宽泛隐式转换与晚期重载 |
| Declarations | 源文件声明树 | Declaration, DeclarationKind, DeclarationModifiers(含 WithEvents), DeclarationTable, MergedNamespaceDeclaration, SingleTypeDeclaration | 中 |
| Compilation | 编译入口/语义模型/脚本 | VisualBasicCompilation(CreateScriptCompilation), VisualBasicScriptCompilationInfo(PreviousScriptCompilation), SemanticModel, SyntaxTreeSemanticModel, SpeculativeSemanticModel, MethodCompiler, SymbolInfo, ForEachStatementInfo | 高——编译器入口；CreateScriptCompilation 与 vbx 脚本编译链相关 |
| Errors | 诊断/BC 错误码 | MessageProvider(CodePrefix="BC"), ERRID, ErrorFactories, VBDiagnostic, ERR_ 枚举 | 中——VBScript 新错误码在此注册 |
| Locations | 位置抽象 | VBLocation, EmbeddedTreeLocation, MyTemplateLocation | 低 |
| Operations | BoundNode→IOperation | VisualBasicOperationFactory, IBoundNodeWithIOperationChildren | 中 |
| Utilities | 杂项工具 | TypeUnification, VarianceAmbiguity | 低 |
| Generated | 生成代码 + 文法 | Syntax.xml.Main/Internal, BoundNodes.xml.Generated, VisualBasic.Grammar.g4(ANTLR) | 中——g4 文法对 VBScript 语法设计有参考价值；改节点须同步 .xml 源 |
| DocumentationComments | XML 注释 ID | DocumentationCommentIDVisitor | 低 |
| Analysis | 方法体流分析 | Analyzer, FlowAnalysisPass, ForLoopVerification, InitializerRewriter | 中——方法体分析 |
| CommandLine | 命令行/.vbx/vbi 入口 | VisualBasicCommandLineParser(ScriptFileExtension=".vbx"), \i 交互模式, vbi, SourceCodeKind.Script, VisualBasicCompiler(scriptParseOptions), VisualBasicCommandLineArguments | 高——搜"vbx 脚本"/"vbi"定位点；.vbx 扩展名与 vbi 交互模式均在此 |

### VB-EMIT-SYMBOLS（VB 符号/降级/发射侧）

```
VisualBasic\Portable\
├── Symbols/          —— 符号系统（所有符号基类 + 源码/元数据/合成实现）
│   ├── Source/       —— 源码声明符号
│   ├── Metadata/PE/  —— 元数据导入符号
│   ├── Retargeting/  —— 重定向包装符号
│   ├── AnonymousTypes/ —— 匿名类型/匿名委托管理
│   ├── Attributes/   —— 特性数据
│   ├── EmbeddedSymbols/ —— 嵌入语法树符号（VB Core/My/XML helper）
│   ├── SynthesizedSymbols/ —— 编译器生成名称（GeneratedNames）
│   ├── Wrapped/      —— 包装符号基类
│   └── Tuples/       —— VB 元组符号
├── SymbolDisplay/    —— 符号显示/格式化
├── Lowering/         —— 绑定树降级
│   ├── LocalRewriter/ —— 逐语句本地重写（VB 特有语句）
│   ├── LambdaRewriter/ —— Lambda/闭包重写（Frame 类）
│   ├── AsyncRewriter/ —— Async/Await 重写
│   ├── IteratorRewriter/ —— Yield 迭代器重写
│   ├── StateMachineRewriter/ —— 状态机重写抽象基类
│   ├── MethodToClassRewriter/ —— 方法体提升到类的重写基类
│   ├── ExpressionLambdaRewriter/ —— Lambda→表达式树
│   ├── Instrumentation/ —— 代码插桩
│   └── Diagnostics/  —— 降级后诊断检查
├── CodeGen/          —— IL 代码生成（CodeGenerator）
├── Emit/             —— 符号到 CCI/PE 发射（PEModuleBuilder/PEAssemblyBuilder）
├── SourceGeneration/ —— 源生成器驱动
└── CommandLine/      —— 命令行解析与编译器入口（vbc）
```

| 目录 | 一句话职责 | 关键词 | 相关性 |
|---|---|---|---|
| Symbols（根） | 符号基类与通用符号 | Symbol, TypeSymbol, NamedTypeSymbol, MethodSymbol, FieldSymbol, PropertySymbol, EventSymbol, ParameterSymbol, ModuleSymbol, NamespaceSymbol, AssemblySymbol, TypeSubstitution, CustomModifier, ConstraintsHelper, WellKnownMembers, ReferenceManager | 高——符号模型是绑定/编译核心 |
| Symbols/Source | 源码声明符号 + 合成符号 | SourceMethodSymbol, SourceNamedTypeSymbol, SourceAssemblySymbol, SourcePropertySymbol, LocalSymbol, LambdaSymbol, SynthesizedLambdaSymbol, MeParameterSymbol, SynthesizedConstructorSymbol, SynthesizedEntryPointSymbol, SynthesizedSubmissionConstructorSymbol | 高——VBScript 必须为自身声明（Sub/Function/Class/Dim）建 Source 符号 |
| Symbols/Metadata/PE | 元数据导入符号 | PEAssemblySymbol, PEModuleSymbol, PENamedTypeSymbol, PEMethodSymbol, PESymbolFactory, MetadataDecoder, TupleTypeDecoder | 中——引用 CLR/BCL 类型需要 |
| Symbols/Retargeting | 程序集重定向 | RetargetingAssemblySymbol, RetargetingNamedTypeSymbol, RetargetingMethodSymbol | 低 |
| Symbols/AnonymousTypes | 匿名类型/匿名委托 | AnonymousTypeManager, AnonymousTypeDescriptor | 中 |
| Symbols/Attributes | 特性数据统一抽象 | VisualBasicAttributeData, SourceAttributeData, PEAttributeData, RetargetingAttributeData | 高——特性绑定是 VBScript 标记语法的底层机制 |
| Symbols/EmbeddedSymbols | 嵌入语法树符号 | EmbeddedSymbolManager, EmbeddedSymbolKind, VbCoreSourceText, VbMyTemplateText | 低——VB 运行时/My/XML 内嵌源码服务 |
| Symbols/SynthesizedSymbols | 生成名称构造/解析 | GeneratedNames, GeneratedNameParser, GeneratedNameKind, MakeStateMachineTypeName, MakeLambdaDisplayClassName | 高——任何状态机/闭包/合成符号降级都需要稳定内部名称 |
| Symbols/Wrapped | 包装符号基类 | WrappedNamedTypeSymbol, UnderlyingNamedType | 中 |
| Symbols/Tuples | VB 元组符号 | TupleTypeSymbol, TupleFieldSymbol, TupleErrorFieldSymbol | 中 |
| SymbolDisplay | 符号格式化显示 | SymbolDisplayVisitor, ObjectDisplay, CustomSymbolDisplayFormatter | 中 |
| Lowering（根） | 降级管线基础设施 | Rewriter, SyntheticBoundNodeFactory, SynthesizedSubmissionFields, UseTwiceRewriter, WithExpressionRewriter | 高——降级管线入口，VBScript 一切"翻译"在此层面做 |
| Lowering/LocalRewriter | VB 特有语句语义归一化 | LocalRewriter, LocalRewriter_ForEach/ForTo/SelectCase/SyncLock/Using/With/Redim/Erase/Throw/LateBinding/LateInvocation/OnError/UnstructuredExceptionHandling/Query/XmlLiterals/InterpolatedString/StringConcat | 高——ReDim/On Error/With/MyBase/Handles/晚绑定都是 VBScript 常用语义 |
| Lowering/LambdaRewriter | Lambda→闭包类+委托 | LambdaRewriter, LambdaFrame, LambdaCapturedVariable, SynthesizedLambdaMethod | 高——VBScript 支持 Function 表达式/回调必用 |
| Lowering/AsyncRewriter | Async/Await→状态机 | AsyncRewriter, AsyncStateMachine, AsyncMethodKind, CapturedSymbolOrExpression, SpillBuilder, AsyncTaskMethodBuilder | 高——支持 Async/Await 则完全依赖此路径 |
| Lowering/IteratorRewriter | Yield→迭代器 | IteratorRewriter, IteratorStateMachine, elementType | 高——支持 Yield 则相关 |
| Lowering/StateMachineRewriter | 状态机重写基类 | StateMachineRewriter(Of TProxy), StateMachineTypeSymbol, StateMachineFieldSymbol, SynthesizedStateMachineMethod, hoistedVariables | 高——async/yield 降级都从它派生 |
| Lowering/MethodToClassRewriter | 方法-类重写底座 | MethodToClassRewriter(Of TProxy), Proxies, LocalMap, ParameterMap | 高——Lambda/Iterator/Async 重写公共底座 |
| Lowering/ExpressionLambdaRewriter | Lambda→表达式树 | ExpressionLambdaRewriter | 中——支持表达式树才相关 |
| Lowering/Instrumentation | 代码插桩 | Instrumenter, CodeCoverageInstrumenter, DebugInfoInjector | 低 |
| Lowering/Diagnostics | 降级后诊断检查 | DiagnosticsPass, DiagnosticBag, BoundTreeWalkerWithStackGuard | 高——降级后仍要出诊断 |
| CodeGen | 降级树→IL | CodeGenerator, EmitAddress, EmitArrayInitializer, EmitConversion, EmitExpression, EmitOperators, EmitStatement, OperatorKind, ILBuilder | 高——IL 发射核心，VBScript 最终输出指令必经此层 |
| Emit | 符号→CCI/PE | PEModuleBuilder, PEAssemblyBuilder, SymbolTranslator, SymbolAdapter, AssemblyReference, MethodReference, NamedTypeReference, AttributeDataAdapter | 高——把符号翻译成可序列化 PE 元数据的骨架 |
| SourceGeneration | 源生成器驱动 | VisualBasicGeneratorDriver, VisualBasicSyntaxHelper | 中 |
| CommandLine | 命令行/vbc 入口 | VisualBasicCommandLineParser, VisualBasicCompiler, VisualBasicCommandLineArguments, ResponseFileName | 高——VBScript 需自己的命令行入口/编译器宿主，可仿此裁剪 |

### DRIVERS-TESTS（命令行驱动、共享工具、测试基础设施）

```
Compilers\
├── CSharp\csc\        —— C# 命令行驱动（Program→BuildClient→Csc.Run，csc.rsp）
│   ├── AnyCpu\        —— AnyCPU 平台 csproj
│   └── arm64\         —— arm64 平台 csproj
├── VisualBasic\vbc\   —— VB 命令行驱动（vbc 入口，响应文件 rsp 配置）
├── Shared\            —— 编译器共享：构建服务器客户端 + 编译入口 + GAC 解析
│   └── GlobalAssemblyCacheHelpers\ —— GAC/Fusion 程序集解析
├── Test\
│   ├── Core\          —— 跨语言测试基础设施（Microsoft.CodeAnalysis.Test.Utilities）
│   │   ├── Assert\ Compilation\ Diagnostics\ Extensions\ FX\ MarkedSource\ Metadata\ Mocks\ PDB\ Pe\ Platform\ SourceGeneration\ Syntax\ TempFiles\ Traits\
│   │   └── Compilation\FlowAnalysis\ —— 数据流分析器（测试用）
│   ├── Utilities\     —— 语言专用测试实用类
│   │   └── VisualBasic\ —— VB 测试基类（BasicTestBase）
│   └── Resources\     —— 测试资源工程（预编译 dll/il/winmd/源）
├── CSharp\Test\       —— C# 测试项目群（CommandLine/Emit/Emit2/Emit3/EndToEnd/IOperation/Semantic/Symbol/Syntax/WinRT/CSharp15）
└── VisualBasic*Test\  —— VB 测试项目群（CommandLine/Emit/IOperation/Semantic/Symbol/Syntax/综合）
```

| 目录 | 一句话职责 | 关键词 | 相关性 |
|---|---|---|---|
| CSharp\csc | C# 命令行驱动入口 | Program, Csc.Run, BuildClient.Run, RequestLanguage.CSharpCompile, /noconfig | 高——VBScript 驱动入口照此写 |
| VisualBasic\vbc | VB 命令行驱动入口 | Program, Vbc.Run, BuildClient.Run, RequestLanguage.VisualBasicCompile, vbc.rsp(/r:、/imports:System、/optioninfer+), vbc.runtime.rsp(/sdkpath) | 高——VB 语法最接近 VBScript，vbc rsp/imports 配置是最佳模板 |
| Shared | 编译器共享入口 + 构建服务器客户端 | Csc, Vbc, BuildClient, BuildServerConnection, BuildProtocol, NamedPipeUtil, RuntimeHostInfo, CoreClrShim, CommonCompiler | 高——VBScript 驱动直接复用 BuildClient/CommonCompiler |
| Shared/GlobalAssemblyCacheHelpers | GAC/Fusion 解析 | GacFileResolver, GlobalAssemblyCacheLocation, FusionAssemblyIdentity | 低 |
| Test\Core（根） | 跨语言测试基类 | TestBase, CommonTestBase, CompilationVerifier, TestableCompiler, TestableFileSystem, CompilerTraitAttribute, TargetFrameworkUtil | 高——"编译器测试框架"核心；注意本仓库基类是 TestBase/CommonTestBase（VB 侧 BasicTestBase），不存在 CompilationTestFixture |
| Test\Core\Assert | xUnit 断言/特性工具 | AssertEx, AssertXml, EqualityUnit, ConditionalFactAttribute, UseCultureAttribute | 中 |
| Test\Core\Compilation | 编译/语义/CFG/操作树验证 | CompileAndVerify, CompilationVerifier, ControlFlowGraphVerifier, OperationTreeVerifier, NullErrorLogger | 高——验证编译输出/CFG/IOperation 的核心工具 |
| Test\Core\Diagnostics | 诊断描述与测试分析器 | DiagnosticDescription, DescriptorFactory, CommonDiagnosticAnalyzers, TrackingDiagnosticAnalyzer | 中 |
| Test\Core\Extensions | 符号/操作扩展方法 | OperationExtensions, SymbolExtensions | 低 |
| Test\Core\FX | 框架/进程/文化/编码辅助 | ProcessUtilities, DirectoryHelper, CultureHelpers, EncodingUtilities, PinnedMetadata | 中 |
| Test\Core\MarkedSource | 标记源解析 | MarkupTestFile, SourceWithMarkedNodes | 中——[|标记|] 定位节点的标准方式 |
| Test\Core\Metadata | 元数据/IL 验证 | ILValidation, MetadataValidation, IlasmUtilities, ModuleData | 中 |
| Test\Core\Mocks | 测试替身 | TestAnalyzerAssemblyLoader, TestMetadataReference, TestReferences, TestSourceReferenceResolver | 中 |
| Test\Core\PDB | PDB/确定性构建辅助 | DeterministicBuildCompilationTestHelpers | 中 |
| Test\Core\Pe | PE 流测试辅助 | BrokenStream | 低 |
| Test\Core\Platform | 运行时环境封装 | CoreClr\CoreCLRRuntimeEnvironment, Desktop\DesktopRuntimeEnvironment, RuntimeAssemblyManager | 中 |
| Test\Core\SourceGeneration | 源生成器测试桩 | TestSourceGenerator, TestGenerators | 中 |
| Test\Core\Syntax | 语法节点测试辅助 | NodeInfo, NodeHelpers, TokenUtilities | 中 |
| Test\Core\TempFiles | 临时文件/目录管理 | TempRoot, TempDirectory, TempFile, DisposableDirectory | 高——命令行/PE 输出测试统一走 TempRoot |
| Test\Core\Traits | xUnit trait 发现 | Traits, CompilerFeature, CompilerTraitDiscoverer | 中——可加 VBScript 特征 |
| Test\Utilities\VisualBasic | VB 语言测试实用类 | BasicTestBase, BasicTestSource, CompilationTestUtils, MockVbi, TestOptions, SemanticModelTestBase | 高——VBScript 测试基类应仿照 BasicTestBase 派生；注意本仓库无 Test\Utilities\CSharp |
| Test\Resources\Core | 预编译测试资源 | Analyzers, DiagnosticTests, MetadataTests, NetFX, SymbolsTests, ResourceLoader | 中 |
| CSharp\Test\CommandLine | C# 命令行测试 | CommandLineTests, SarifErrorLoggerTests, TouchedFileLoggingTests | 中——rsp/响应文件/SARIF 测试模式是驱动测试模板 |
| VisualBasicCommandLineTest | VB 命令行测试 | CommandLineTests, CommandLineArgumentsTests, SarifErrorLoggerTests | 高——VBScript 命令行驱动测试直接模板 |
| VisualBasicEmitTest | VB 代码生成测试 | ErrorHandling, XmlLiteralTests, BreakingChanges | 高 |
| VisualBasicIOperationTest | VB IOperation 测试 | AssemblyAttributes, IOperation | 中 |
| VisualBasicSemanticTest | VB 语义测试 | DeclaringSyntaxNodeTests, SemanticResourceUtil | 高 |
| VisualBasicSymbolTest | VB 符号测试 | CompilationAPITests, CrossLanguageTest, StaticLocalDeclarationTests | 高 |
| VisualBasicSyntaxTest | VB 语法测试 | LocationTests, PreprocessorEETests, QuickTokenTableTests | 高 |
| VisualBasicTest | VB 综合测试 | ObjectDisplayTests, SymbolDisplayTests | 高 |

## 三、与 VBScript.NET 的关系

- **最接近的改造基底是 VB 编译器**（`VisualBasic\Portable`）：VBScript.NET 语法与 VB 最接近，Parser/Scanner/Binding/Lowering 的裁剪入口都在这侧。C# 编译器主要供**对比参考**（其异步/迭代器/模式匹配降级实现更完整）。
- **脚本能力已内建**：`SourceCodeKind.Script` + `.vbx` 扩展名 + `vbi` 交互模式 + `CreateScriptCompilation` + `VisualBasicScriptCompilationInfo` 已存在于本树——VBScript.NET 的脚本编译链可直接沿用/扩展，不用从零搭。
- **通用底层直接复用**：Syntax 双树、Text、Compilation、Diagnostic、Emit/PEWriter/CodeGen、Symbols、MetadataReader/MetadataReference/ReferenceManager、DiagnosticAnalyzer、Operations、SourceGeneration、CommandLine/CommonCompiler、BuildClient 全是语言无关层，VBScript.NET 无需重写。
- **需语言定制**：语法节点层次与关键字表（SyntaxFacts/KeywordTable）、Parser/Scanner、Binder 与晚期绑定语义、BoundTree 节点、降级层（ReDim/On Error/With 等 VBScript 语义）、Synthesized 命名编码（GeneratedNames）、错误码（Errors.vb/BC 前缀）、命令行驱动（仿 vbc/rsp）。
- **测试策略**：仿照 `BasicTestBase` 派生 VBScript 测试基类；用 `Test\Core\Compilation\CompileAndVerify` 验证发射；用 MarkupTestFile 精确定位语法节点；按 7 个 VB 测试项目维度组织。

## 四、索引纪律提示

- 本索引**只记目录与关键词，不记具体文件**：源码文件增删不影响索引有效性，只有目录结构变化才需更新。
- 关键词是 Grep 定位用词：新增/改名核心类型时，同步更新对应行关键词。
- 版本信息（一、版本快照）随 `LanguageVersion` 变化更新。
- 若上游 Roslyn 结构发生大改（目录重命名/拆分），以本树实际 `find -type d` 结果为准重扫对应切片。

## OPEN QUESTIONS

- `SourceCodeKind.Interactive` 已被 `[Obsolete]` 标记（"Use Script instead"）——`.vbx` 场景应统一走 `Script`，无需兼容 `Interactive`。
- `VisualBasic\Portable\Emit` 与 `CSharp\Portable\Emitter` 命名不对称（VB 无独立 `Emitter` 目录，发射在 `Emit` 下），后续深挖时注意区分。
- 本树 `Test\Utilities` 无 `CSharp` 子目录（C# 测试工具并入 `Test\Core`）——与上游 Roslyn 布局有差异，测试组织参考时以本树为准。
