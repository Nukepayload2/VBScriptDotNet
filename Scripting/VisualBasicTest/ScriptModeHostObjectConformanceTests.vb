' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System
Imports System.Collections.Immutable
Imports System.Linq
Imports System.Text
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.Scripting
Imports Microsoft.CodeAnalysis.VisualBasic.Scripting
Imports Xunit

''' <summary>
''' U3: host object (<c>globalsType</c>) binding semantics. One cell per C# <c>HostObjectBinding_*</c> method
''' (<c>CSharpTest\InteractiveSessionTests.cs:1528,1545,1553,1570,1580,1590,1598,1617</c>) plus
''' <c>HostObjectInRootNamespace</c> (<c>:1628</c>) and the static/instance access pair (<c>:1855,1873,1895</c>).
''' </summary>
''' <remarks>
''' <para>
''' <b>The C# to VB mapping is not a same-shape translation, and each cell says which part is not.</b> The C#
''' <c>static</c> modifier is <c>Shared</c> in VB; VB has no local function, so the C# static local function
''' (<c>:1873</c>) is dualed by a lambda declared inside a top level <c>Shared</c> method; and the host object
''' type is resolved from the reflection type on both sides
''' (<c>Compilers\VisualBasic\Portable\Compilation\VisualBasicCompilation.vb:933</c>, <c>CSharpCompilation.cs:1873</c>).
''' </para>
''' <para>
''' <b>Reflection shapes are resolved by <c>Assembly.GetTypeByReflectionType</c>, not by name.</b> The
''' <c>FullName</c> of a constructed generic, of an array or of a nested type is a reflection form - it spells the
''' type arguments out together with their assembly names - so it is not a metadata name and
''' <c>GetTypeByMetadataName</c> cannot resolve it. The cells below therefore cover the shapes whose
''' <c>FullName</c> is not an ordinary metadata name (constructed generics at file level and nested, arrays,
''' nested types inside constructed generic outer types) and assert which type argument landed on which level,
''' which is what makes the resolution mechanism, rather than the mere absence of an error, the subject of the
''' assertion. The name based lookup with the '+' to '.' fallback
''' (<c>VisualBasicCompilation.vb:945-952</c>) is kept as the fallback behind it.
''' </para>
''' <para>
''' <b>Discriminative argument.</b> Every positive cell below is falsified by removing the host object from the
''' binding: pass <c>globalsType:=Nothing</c> and the same submission reports <c>BC30451</c> instead of the pinned
''' value. Every negative cell is falsified by the opposite change: merge the host object's members into the
''' submission chain's lookup and the expected diagnostic disappears (the member binds, or the overload set
''' becomes larger than the one the assertion pins). The host object member reference itself is built by
''' <c>Binding\Binder_Expressions.vb:2615-2634</c> and rewritten by
''' <c>Lowering\LocalRewriter\LocalRewriter_HostObjectMemberReference.vb:13-19</c>, so a cell that stops binding
''' turns into <c>BC30451</c>/<c>BC30469</c> and the pinned value assertion fails with the diagnostics echoed in
''' the failure message.
''' </para>
''' <para>
''' Everything here is in memory: the host objects are ordinary VB types of this test project (no external
''' library), submissions run through <c>VisualBasicScript</c>, and no file, process, registry or network access
''' happens (test-plan section 2).
''' </para>
''' </remarks>
Public Class ScriptModeHostObjectConformanceTests

#Region "host object submission chains"

    ''' <summary>
    ''' Joins the lines of a single submission. A <c>ParamArray</c> element of the assertions below is one
    ''' submission, so a multi line declaration (a method body, a property block) has to be joined explicitly.
    ''' </summary>
    Private Shared Function Lines(ParamArray text As String()) As String
        Return String.Join(vbCrLf, text)
    End Function

    ''' <summary>
    ''' Builds a submission chain whose every link keeps the same <c>globalsType</c>, which is what the C# cells do
    ''' with <c>RunAsync</c> + <c>ContinueWithAsync</c> (<c>InteractiveSessionTests.cs:1532-1541</c>).
    ''' </summary>
    Private Shared Function CreateHostChain(globalsType As Type, submissions As String()) As Script(Of Object)
        Dim script = VisualBasicScript.Create(submissions(0), ScriptModeConformance.DefaultOptions, globalsType)
        For index = 1 To submissions.Length - 1
            script = script.ContinueWith(submissions(index))
        Next

        Return script
    End Function

    ''' <summary>
    ''' The cell runs and its last return value is pinned. A host object that silently stops binding does not throw
    ''' here - it produces <c>BC30451</c> - so the diagnostics are part of the failure message.
    ''' </summary>
    Private Shared Sub AssertHostChainRuns(globalsType As Type, globals As Object, expectedValue As Object, ParamArray submissions As String())
        Dim script = CreateHostChain(globalsType, submissions)
        Dim diagnostics = script.Compile()

        Assert.True(ErrorDiagnostics(diagnostics).Length = 0,
                    HostReport("the host object cell must compile", submissions, globalsType, diagnostics, Nothing))

        Dim state As ScriptState = Nothing
        Dim failure As Exception = Nothing
        Try
            state = script.RunAsync(globals).GetAwaiter().GetResult()
        Catch ex As Exception
            failure = ex
        End Try

        Assert.True(failure Is Nothing,
                    HostReport("the host object cell must run", submissions, globalsType, diagnostics, failure))
        Assert.Equal(expectedValue, state.ReturnValue)
    End Sub

    ''' <summary>
    ''' The cell is rejected with one specific diagnostic, both on the compilation and on the exception the host
    ''' receives.
    ''' </summary>
    Private Shared Sub AssertHostChainReports(globalsType As Type, globals As Object, expectedId As String, ParamArray submissions As String())
        Dim script = CreateHostChain(globalsType, submissions)
        Dim diagnostics = script.Compile()

        Assert.True(diagnostics.Any(Function(d) d.Severity = DiagnosticSeverity.Error AndAlso d.Id = expectedId),
                    HostReport("expected " & expectedId, submissions, globalsType, diagnostics, Nothing))

        Dim failure As Exception = Nothing
        Try
            script.RunAsync(globals).GetAwaiter().GetResult()
        Catch ex As Exception
            failure = ex
        End Try

        Assert.True(TypeOf failure Is CompilationErrorException,
                    HostReport("the rejected cell must surface as a diagnostic", submissions, globalsType, diagnostics, failure))
        Assert.True(DirectCast(failure, CompilationErrorException).Diagnostics.Any(Function(d) d.Id = expectedId),
                    HostReport("the host did not receive " & expectedId, submissions, globalsType, diagnostics, failure))
    End Sub

    Private Shared Function ErrorDiagnostics(diagnostics As ImmutableArray(Of Diagnostic)) As Diagnostic()
        Return diagnostics.Where(Function(d) d.Severity = DiagnosticSeverity.Error).ToArray()
    End Function

    Private Shared Function HostReport(what As String, submissions As String(), globalsType As Type, diagnostics As ImmutableArray(Of Diagnostic), failure As Exception) As String
        Dim builder = New StringBuilder()
        builder.AppendLine(what)
        builder.AppendLine("--- globalsType ---")
        builder.AppendLine(If(globalsType Is Nothing, "(none)", globalsType.AssemblyQualifiedName))

        For index = 0 To submissions.Length - 1
            builder.AppendLine("--- submission " & index & " ---")
            For Each line In submissions(index).Replace(vbCrLf, vbLf).Split(ControlChars.Lf)
                builder.AppendLine("| " & line)
            Next
        Next

        builder.AppendLine("--- diagnostics ---")
        If diagnostics.IsDefaultOrEmpty Then
            builder.AppendLine("(none)")
        Else
            For Each diagnostic In diagnostics
                builder.AppendLine($"{diagnostic.Id} {diagnostic.Severity} @{diagnostic.Location.GetLineSpan().StartLinePosition.Line}: {diagnostic.GetMessage()}")
            Next
        End If

        If failure IsNot Nothing Then
            builder.AppendLine("--- run ---")
            builder.AppendLine(failure.GetType().FullName & ": " & failure.Message)
        End If

        Return builder.ToString()
    End Function

#End Region

#Region "cell 1-2: public and public generic host object members"

    ''' <summary>
    ''' Cell <c>HostObjectBinding_PublicClassMembers</c> (<c>InteractiveSessionTests.cs:1528</c>). Direct
    ''' correspondence: a field inherited from the host object's base, a property and a method are all in scope of
    ''' an unqualified name in a submission, and a later submission that declares a field of the same name takes
    ''' over - the host object field is declared on the host object's <em>base</em> type, so this cell is also what
    ''' pins the base chain walk in <c>Binder_Expressions.vb:2623-2629</c>.
    ''' </summary>
    <Fact>
    Public Sub HostObjectBinding_PublicClassMembers()
        AssertHostChainRuns(
            GetType(HostObjectFixtures.PublicMembers), New HostObjectFixtures.PublicMembers(), 6,
            "? x + Y + Z()")

        AssertHostChainRuns(
            GetType(HostObjectFixtures.PublicMembers), New HostObjectFixtures.PublicMembers(), 1,
            "? x")

        ' C# declares 'int x = 20;' in a later submission and reads 20 back (InteractiveSessionTests.cs:1538-1541).
        ' The submission's own field wins, so this is the positive half of the shadowing rule the binding relies on.
        AssertHostChainRuns(
            GetType(HostObjectFixtures.PublicMembers), New HostObjectFixtures.PublicMembers(), 20,
            "Dim x As Integer = 20",
            "? x")
    End Sub

    ''' <summary>
    ''' Cell <c>HostObjectBinding_PublicGenericClassMembers</c> (<c>InteractiveSessionTests.cs:1545</c>): a
    ''' constructed generic at file level as <c>globalsType</c> binds its members, and the type argument reaches the
    ''' host object's own type parameter.
    ''' <para>
    ''' The premise is verified at run time instead of assumed: the <c>FullName</c> of a constructed generic is the
    ''' reflection form with the type arguments spelled out, so it is not a metadata name and the name based lookup
    ''' cannot resolve it. This cell used to pin <c>BC30451</c> as the evidence of that defect
    ''' (<c>InternalDevDocs\issues\issue-constructed-generic-host-object-not-bound.md</c>, issue 22); it now pins
    ''' the C# expectation, which is what the issue records as the fixed behaviour. The second assertion is the
    ''' discriminative one: it fails if the type argument is dropped or applied at the wrong level, which a mere
    ''' "no diagnostic" assertion would not catch.
    ''' </para>
    ''' </summary>
    <Fact>
    Public Sub HostObjectBinding_PublicGenericClassMembers()
        Dim globalsType = GetType(HostObjectGenericMembers(Of String))
        Assert.Contains("[[", globalsType.FullName, StringComparison.Ordinal)

        ' C# asserts the member value itself (InteractiveSessionTests.cs:1545); the VB fixture returns the type
        ' parameter's default value, so the C# assertion shape is '? G() Is Nothing'.
        AssertHostChainRuns(globalsType, New HostObjectGenericMembers(Of String)(), True, "? G() Is Nothing")

        AssertHostChainRuns(globalsType, New HostObjectGenericMembers(Of String)(), "String", "? TypeArgumentName()")
    End Sub

    ''' <summary>
    ''' The nested counterpart of the cell above, and the second symptom of issue 22: a generic class nested in a
    ''' non generic class. Its <c>FullName</c> carries both the reflection nesting separator and the type argument
    ''' form, so the name based lookup entered the nested type walk
    ''' (<c>Symbols\AssemblySymbol.vb:582-611</c>) and fed the bracket form to
    ''' <c>MetadataTypeName.FromTypeName</c>, whose
    ''' <c>Debug.Assert(!typeName.Contains(".") OrElse typeName.IndexOf("&lt;") &gt;= 0)</c> is triggered
    ''' (<c>Core\Portable\MetadataReader\MetadataTypeName.cs:154</c>; the quoted character is
    ''' <c>MetadataHelpers.MangledNameRegionStartChar</c>, whose literal is <c>'&lt;'</c> -
    ''' <c>Core\Portable\MetadataReader\MetadataHelpers.cs:57</c>). In a host without a trace listener that
    ''' assertion terminates the process, so this cell is also the regression guard against the crash: it could not
    ''' exist before the fix because it aborts the test host instead of failing.
    ''' </para>
    ''' </summary>
    <Fact>
    Public Sub HostObjectBinding_NestedGenericClassMembers()
        Dim globalsType = GetType(HostObjectNestedMembers.NestedGenericMembers(Of String))
        Assert.Contains("+", globalsType.FullName, StringComparison.Ordinal)
        Assert.Contains("[[", globalsType.FullName, StringComparison.Ordinal)

        AssertHostChainRuns(
            globalsType, New HostObjectNestedMembers.NestedGenericMembers(Of String)(), True, "? G() Is Nothing")

        AssertHostChainRuns(
            globalsType, New HostObjectNestedMembers.NestedGenericMembers(Of String)(), "String", "? TypeArgumentName()")
    End Sub

    ''' <summary>
    ''' The control for the two cells above, and the one shape whose <c>FullName</c> <em>is</em> an ordinary
    ''' metadata name: the same generic class, closed by <c>Inherits</c> instead of by a type argument on
    ''' <c>globalsType</c>, is resolved by the name based lookup exactly as before the reflection route existed, so
    ''' its inherited generic member binds.
    ''' </summary>
    <Fact>
    Public Sub HostObjectBinding_ClosedGenericBaseMembers()
        Assert.Equal("Microsoft.CodeAnalysis.VisualBasic.Scripting.UnitTests.HostObjectClosedGenericMembers",
                     GetType(HostObjectClosedGenericMembers).FullName)

        AssertHostChainRuns(
            GetType(HostObjectClosedGenericMembers), New HostObjectClosedGenericMembers(), True,
            "? G() Is Nothing")
    End Sub

#End Region

#Region "cell 13-16: the remaining reflection shaped globals types"

    ''' <summary>
    ''' A nested type whose <em>enclosing</em> type is generic
    ''' (<c>GetType(HostObjectGenericOuter(Of String).NestedPlainMembers)</c>). This shape is one step past the two
    ''' symptoms of issue 22 and is what pushes the assertion out of the name based lookup and into
    ''' <c>AssemblySymbol.GetTypeByReflectionType</c>: reflection reports the enclosing type as the generic type
    ''' <em>definition</em> (<c>GetType(HostObjectGenericOuter(Of String).NestedPlainMembers).DeclaringType</c> is
    ''' <c>HostObjectGenericOuter(Of T)</c>), which is an open type the reflection walk rejects. Closing it with the
    ''' arguments the original type carries for it (<c>Symbols\AssemblySymbol.vb:765-774</c>) is what the cell
    ''' pins, and the second assertion - the enclosing type's own type argument seen from the nested type - is the
    ''' part that fails if the enclosing type is left unsubstituted.
    ''' </summary>
    <Fact>
    Public Sub HostObjectBinding_NestedTypeInConstructedGenericOuter()
        Dim globalsType = GetType(HostObjectGenericOuter(Of String).NestedPlainMembers)
        Assert.Contains("+", globalsType.FullName, StringComparison.Ordinal)
        Assert.Contains("[[", globalsType.FullName, StringComparison.Ordinal)

        AssertHostChainRuns(
            globalsType, New HostObjectGenericOuter(Of String).NestedPlainMembers(), 1, "? G()")

        AssertHostChainRuns(
            globalsType, New HostObjectGenericOuter(Of String).NestedPlainMembers(), "String", "? OuterTypeArgumentName()")
    End Sub

    ''' <summary>
    ''' The same shape one level further: a generic nested type inside a constructed generic enclosing type. Each
    ''' level carries its own type argument, so the cell asserts which argument landed on which level - the enclosing
    ''' type's and the nested type's. This is the part of the reflection walk that the consolidated argument index
    ''' has to get right (<c>Symbols\AssemblySymbol.vb:774</c>): the outer argument is consumed before the walk
    ''' descends into the nested type.
    ''' </summary>
    <Fact>
    Public Sub HostObjectBinding_GenericNestedTypeInConstructedGenericOuter()
        Dim globalsType = GetType(HostObjectGenericOuter(Of String).NestedGenericMembers(Of Integer))

        AssertHostChainRuns(
            globalsType, New HostObjectGenericOuter(Of String).NestedGenericMembers(Of Integer)(), "Int32|String",
            "? OwnTypeArgumentName() & ""|"" & OuterTypeArgumentName()")

        ' The same type at the same nesting depth with the two arguments swapped: the pin above is not satisfiable
        ' by an implementation that applies either argument at both levels.
        AssertHostChainRuns(
            GetType(HostObjectGenericOuter(Of Integer).NestedGenericMembers(Of String)),
            New HostObjectGenericOuter(Of Integer).NestedGenericMembers(Of String)(), "String|Int32",
            "? OwnTypeArgumentName() & ""|"" & OuterTypeArgumentName()")
    End Sub

    ''' <summary>
    ''' Three levels of nesting with generic arguments at three different levels, which is the shape of the
    ''' reflection <c>Type</c> resolution test on the C# side
    ''' (<c>Compilers\CSharp\Test\Symbol\Symbols\TypeResolutionTests.cs:142</c>,
    ''' <c>TypeSymbolFromReflectionType</c>; that test is gated on
    ''' <c>[ConditionalFact(typeof(ClrOnly), typeof(DesktopOnly))]</c> and does not run here). Each of the five type
    ''' arguments is asserted at the level it belongs to, so the walk has to resolve the outermost definition and
    ''' apply arguments in the declaration order of the enclosing chain.
    ''' </summary>
    <Fact>
    Public Sub HostObjectBinding_DeeplyNestedGenericTypeArguments()
        Dim globalsType = GetType(HostObjectDeepOuter(Of Integer, Boolean).Middle.Inner(Of Double, Single).Leaf(Of Byte))

        AssertHostChainRuns(
            globalsType,
            New HostObjectDeepOuter(Of Integer, Boolean).Middle.Inner(Of Double, Single).Leaf(Of Byte)(),
            "Int32,Boolean,Double,Single,Byte", "? Arguments()")
    End Sub

    ''' <summary>
    ''' An array as <c>globalsType</c>. Its <c>FullName</c> (<c>"System.Int32[]"</c>,
    ''' <c>"System.Int32[,,]"</c>) is a reflection form too, so before the reflection route these shapes bound no
    ''' host object at all and reported <c>BC30451</c> for every member. <c>Length</c> is the member that only the
    ''' array type itself provides, which is why it is the one asserted: it fails with <c>BC30451</c> both when the
    ''' host object is missing and when the element type or the rank is lost.
    ''' </summary>
    <Fact>
    Public Sub HostObjectBinding_ArrayGlobalsType()
        ' Premise: arrays pass Compilation.IsValidHostObjectType, which is what separates them from the rejected
        ' shapes in the cell below.
        Assert.True(GetType(Integer()).IsArray)
        Assert.True(GetType(Integer(,,)).IsArray)

        AssertHostChainRuns(GetType(Integer()), New Integer() {1, 2, 3}, 3, "? Length")
        AssertHostChainRuns(GetType(Integer(,,)), New Integer(1, 1, 1) {}, 8, "? Length")

        ' The element type is resolved by the same walk, so an array of a constructed generic is another shape
        ' whose FullName is not a metadata name.
        AssertHostChainRuns(
            GetType(System.Collections.Generic.List(Of String)()),
            New System.Collections.Generic.List(Of String)() {Nothing, Nothing}, 2, "? Length")
    End Sub

    ''' <summary>
    ''' The shapes that <c>globalsType</c> rejects outright, recorded so the boundary of the fix is explicit and
    ''' guarded rather than assumed. <c>Compilation.IsValidHostObjectType</c>
    ''' (<c>Compilers\Core\Portable\Compilation\Compilation.cs:559-563</c>) refuses a value type, a pointer, a
    ''' <c>ByRef</c> type and an open generic, and <c>ValidateScriptCompilationParameters</c> (<c>:250-255</c>)
    ''' turns that into an <c>ArgumentException</c> naming <c>globalsType</c>. This is the pre-existing behaviour on
    ''' both languages and it is why the reflection walk may assert "not ByRef, not open" - those shapes never reach
    ''' it. The assertion is on the exception type only: the message is localized.
    ''' </summary>
    <Fact>
    Public Sub HostObjectBinding_UnsupportedGlobalsTypeShapesAreRejected()
        Dim unsupported = {
            GetType(Integer),                                  ' value type
            GetType(Integer).MakeByRefType(),                   ' ByRef
            GetType(Integer).MakePointerType(),                 ' pointer
            GetType(HostObjectGenericMembers(Of ))             ' open generic
        }

        For Each globalsType In unsupported
            Assert.Throws(Of ArgumentException)(
                Sub() VisualBasicScript.Create("? 1", ScriptModeConformance.DefaultOptions, globalsType).Compile())
        Next
    End Sub

#End Region

#Region "cell 3-6: interface, private type and private member visibility"

    ''' <summary>
    ''' Cell <c>HostObjectBinding_Interface</c> (<c>InteractiveSessionTests.cs:1553</c>). Direct correspondence,
    ''' with the expected diagnostic spelled out instead of assumed: the interface declares neither <c>x</c> nor
    ''' <c>Y</c>, the submission chain does not declare them either, so the lookup runs out of candidates and
    ''' reports <c>BC30451</c> (<c>ERR_NameNotDeclared1</c>, <c>Binder_Expressions.vb:2539</c>). C# reports
    ''' <c>CS0103</c> for the same situation.
    ''' </summary>
    <Fact>
    Public Sub HostObjectBinding_Interface()
        Dim globals = New HostObjectFixtures.PublicMembers()

        ' Interface member in scope, and the interface property is followed across a continuation.
        AssertHostChainRuns(GetType(HostObjectFixtures.IMembers), globals, 3, "? Z()")
        AssertHostChainRuns(GetType(HostObjectFixtures.IMembers), globals, "2", "? N")
        AssertHostChainRuns(GetType(HostObjectFixtures.IMembers), globals, "2", "? Z()", "? N")

        ' The implementation's own members ('x' from the base, 'Y' from the class) are not exposed when the
        ' declared globals type is the interface.
        AssertHostChainReports(GetType(HostObjectFixtures.IMembers), globals, "BC30451", "? x + Y")

        ' Positive partner of the two lines above: the same names bind when the globals type is the class.
        AssertHostChainRuns(GetType(HostObjectFixtures.PublicMembers), globals, 3, "? x + Y")
    End Sub

    ''' <summary>
    ''' Cell <c>HostObjectBinding_PrivateClass</c> (<c>InteractiveSessionTests.cs:1570</c>). Same outcome as C#
    ''' (the member is inaccessible) and a diagnostic of the same family: C# reports <c>CS0122</c>
    ''' (<c>ERR_BadAccess</c>), VB reports <c>BC30390</c> (<c>ERR_InaccessibleMember3</c>,
    ''' <c>Errors\Errors.vb:337</c>) with the observed message
    ''' <c>"'HiddenMembers.Public Function Z() As Integer' is 'Public' so it is not accessible in this context."</c>
    ''' - the message names the member's own accessibility, the reason is the private container named in the same
    ''' string.
    ''' <para>
    ''' This is also the cell that pins the VB host type lookup for a <em>nested</em> type inside a non generic
    ''' enclosing type, the one nested shape whose <c>FullName</c> the name based lookup can also handle
    ''' (<c>VisualBasicCompilation.vb:945-952</c> is the fallback for it): the type is found even though it is
    ''' NestedPrivate - metadata availability and accessibility are different questions.
    ''' </para>
    ''' <para>
    ''' The positive partner of this cell is <c>HostObjectBinding_PrivateClassImplementingPublicInterface</c>
    ''' below: the same private type binds its member once the declared globals type is the public interface.
    ''' </para>
    ''' </summary>
    <Fact>
    Public Sub HostObjectBinding_PrivateClass()
        Assert.Contains("+", HostObjectFixtures.HiddenMembersType.FullName, StringComparison.Ordinal)

        AssertHostChainReports(
            HostObjectFixtures.HiddenMembersType, HostObjectFixtures.CreateHiddenMembers(), "BC30390",
            "? Z()")
    End Sub

    ''' <summary>
    ''' Cell <c>HostObjectBinding_PrivateMembers</c> (<c>InteractiveSessionTests.cs:1580</c>). The C# cell reaches
    ''' the condition through <c>object c = new M&lt;int&gt;()</c>, which makes the globals type
    ''' <c>System.Object</c>; the first half below states the condition directly (globals type is the declaring
    ''' type, the member itself is <c>Private</c>), the second half reproduces the C# shape.
    ''' </summary>
    <Fact>
    Public Sub HostObjectBinding_PrivateMembers()
        ' Positive partner: the public member of the same host object binds, so the negative half below is about
        ' the member's accessibility and not about a host object that failed to bind.
        AssertHostChainRuns(
            GetType(HostObjectFixtures.PublicMembersWithPrivateFunction),
            New HostObjectFixtures.PublicMembersWithPrivateFunction(), 3,
            "? Z()")

        AssertHostChainReports(
            GetType(HostObjectFixtures.PublicMembersWithPrivateFunction),
            New HostObjectFixtures.PublicMembersWithPrivateFunction(), "BC30451",
            "? Hidden()")

        ' The C# shape: the globals type is inferred from the declared variable type, so nothing but Object's own
        ' members is in scope.
        Dim asObject As Object = New HostObjectFixtures.PublicMembersWithPrivateFunction()
        AssertHostChainReports(GetType(Object), asObject, "BC30451", "? Z()")
    End Sub

    ''' <summary>
    ''' Cell <c>HostObjectBinding_PrivateClassImplementingPublicInterface</c>
    ''' (<c>InteractiveSessionTests.cs:1590</c>). Direct correspondence: the declared globals type is the public
    ''' interface, so the private implementation type never has to be nameable, and the instance is accepted by
    ''' <c>Script.cs:560-580</c>'s assignability check.
    ''' </summary>
    <Fact>
    Public Sub HostObjectBinding_PrivateClassImplementingPublicInterface()
        AssertHostChainRuns(
            GetType(HostObjectFixtures.IMembers), HostObjectFixtures.CreateHiddenMembers(), 3,
            "? Z()")
    End Sub

#End Region

#Region "cell 7-8: Shared members and the host object's own overload set"

    ''' <summary>
    ''' Cell <c>HostObjectBinding_StaticMembers</c> (<c>InteractiveSessionTests.cs:1598</c>). C# <c>static</c> maps
    ''' to VB <c>Shared</c>; the C# chain of field, property and nested class becomes the same chain of
    ''' <c>Shared</c> members of the submission class.
    ''' <para>
    ''' The interesting half of the mapping is why this works while the <c>Shared Sub</c> cell below does not: a
    ''' <c>Shared</c> host member needs no receiver, so refusing the implicit host object reference in a Shared
    ''' context (<c>Binder_Expressions.vb:2616</c>, <c>Not currentMember.IsShared</c>) is harmless. The assertion
    ''' value 123 is the host object's field, read through two <c>Shared</c> members of the submission class.
    ''' </para>
    ''' </summary>
    <Fact>
    Public Sub HostObjectBinding_StaticMembers()
        AssertHostChainRuns(
            GetType(HostObjectFixtures.PublicMembers), New HostObjectFixtures.PublicMembers(), 123,
            "Shared goo As Integer = StaticField",
            Lines("Shared ReadOnly Property bar As Integer",
                  "    Get",
                  "        Return goo",
                  "    End Get",
                  "End Property"),
            "? bar")

        ' The C# chain ends with "class C { public static int baz() { return bar; } }" called as "C.baz()"
        ' (InteractiveSessionTests.cs:1602-1605).
        AssertHostChainRuns(
            GetType(HostObjectFixtures.PublicMembers), New HostObjectFixtures.PublicMembers(), 123,
            "Shared goo As Integer = StaticField",
            Lines("Class Nested",
                  "    Public Shared Function baz() As Integer",
                  "        Return goo",
                  "    End Function",
                  "End Class"),
            "? Nested.baz()")
    End Sub

    ''' <summary>
    ''' Cell <c>HostObjectBinding_Overloads</c> (<c>InteractiveSessionTests.cs:1617</c>). Direct correspondence for
    ''' the host object's own overload set; the C# method of that name asserts the non merging rule instead, which
    ''' is the sibling cell <c>HostObjectBinding_HostMembersDoNotJoinSubmissionMethodGroups</c> below.
    ''' </summary>
    <Fact>
    Public Sub HostObjectBinding_Overloads()
        ' Both host object overloads are reachable from one unqualified call site.
        AssertHostChainRuns(
            GetType(HostObjectFixtures.OverloadedMembers), New HostObjectFixtures.OverloadedMembers(), "I2Sa",
            "? Describe(2) & Describe(""a"")")

        ' Adding a submission level overload of the same name replaces the whole set: 'Describe(1)' must take the
        ' submission's Double overload ("D1") and not the host object's exact Integer one ("I1").
        AssertHostChainRuns(
            GetType(HostObjectFixtures.OverloadedMembers), New HostObjectFixtures.OverloadedMembers(), "D1",
            Lines("Function Describe(a As Double) As String",
                  "    Return ""D"" & a",
                  "End Function"),
            "? Describe(1)")
    End Sub

#End Region

#Region "cell 9: host object in the root namespace"

    ''' <summary>
    ''' Cell <c>HostObjectInRootNamespace</c> (<c>InteractiveSessionTests.cs:1628</c>). The C# fixture is a type in
    ''' the global namespace (<c>InteractiveSessionFixtures.cs:5</c>, no <c>namespace</c> declaration), because C#
    ''' has no further layer. VB has one: a type declared without an explicit <c>Namespace</c> block is compiled
    ''' into the project's <c>RootNamespace</c> (<c>Microsoft.CodeAnalysis.VisualBasic.Scripting.UnitTests.vbproj:7</c>),
    ''' and the script compilation is created with <c>rootNamespace:=""</c>
    ''' (<c>VisualBasicScriptCompiler.vb:217</c>).
    ''' <para>
    ''' The two metadata names below are asserted at run time so the premise of the cell is not assumed: both
    ''' shapes bind, which is the actual claim - the host object lookup resolves the type's own assembly name
    ''' (<c>VisualBasicCompilation.vb:933</c> reaches <c>Assembly.GetTypeByReflectionType</c>, and the name based
    ''' fallback behind it uses <c>GlobalsType.FullName</c>) and therefore never consults the script's own root
    ''' namespace.
    ''' </para>
    ''' </summary>
    <Fact>
    Public Sub HostObjectInRootNamespace()
        ' Shape one: declared without a Namespace block, so the project root namespace is prepended.
        Assert.StartsWith("Microsoft.CodeAnalysis.VisualBasic.Scripting.UnitTests.",
                          GetType(ProjectRootNamespaceHostObject).FullName, StringComparison.Ordinal)

        ' Shape two: 'Namespace Global.<name>' opts out of the root namespace, so the metadata name has no prefix.
        Assert.Equal("GlobalQualifiedHostFixture.GlobalQualifiedHostObject",
                     GetType(Global.GlobalQualifiedHostFixture.GlobalQualifiedHostObject).FullName)

        AssertHostChainRuns(
            GetType(ProjectRootNamespaceHostObject), New ProjectRootNamespaceHostObject() With {.X = 1, .Y = 2, .Z = 3}, 6,
            "? X + Y + Z")

        AssertHostChainRuns(
            GetType(Global.GlobalQualifiedHostFixture.GlobalQualifiedHostObject),
            New Global.GlobalQualifiedHostFixture.GlobalQualifiedHostObject() With {.X = 1, .Y = 2, .Z = 3}, 6,
            "? X + Y + Z")
    End Sub

#End Region

#Region "cell 10: host members do not join the submission's method group"

    ''' <summary>
    ''' Cell "host object members don't form a method group with submission members"
    ''' (<c>InteractiveSessionTests.cs:1613-1625</c>, documented there as
    ''' "Host object members don't form a method group with submission members").
    ''' <para>
    ''' The mechanism being pinned is <c>Binder_Lookup.vb:919-926</c>: the host object type is looked into only
    ''' <c>If Not result.HasSymbol</c>, after the whole submission chain has been searched. So a submission level
    ''' member of the same name removes the host object's overloads from the candidate set entirely.
    ''' </para>
    ''' <para>
    ''' The observed values are the discriminating assertion, and the VB shape differs from the C# one because the
    ''' script compilation sets <c>OptionStrict.Off</c> (<c>VisualBasicScriptCompiler.vb:218</c>). With the host
    ''' object in the candidate set, <c>Describe("2")</c> binds the host object's exact <c>String</c> overload and
    ''' returns <c>"S2"</c>. Once the submission declares <c>Describe(Double)</c> that overload is gone; the same
    ''' call is then taken by the submission's <c>Double</c> overload through an implicit <em>narrowing</em>
    ''' conversion, which <c>OptionStrict.Off</c> permits, and returns <c>"D2"</c> - a merged method group would
    ''' keep the exact <c>String</c> overload and stay at <c>"S2"</c>. The third half removes the conversion
    ''' escape hatch: a <c>StringBuilder</c> argument converts to neither <c>Double</c> nor <c>String</c>, so the
    ''' same shape has to report <c>BC30311</c> (value of type 'StringBuilder' cannot be converted to 'Double')
    ''' instead of binding the host object's <c>StringBuilder</c> overload - a single candidate that cannot take
    ''' the argument, not an overload resolution failure, which is itself the proof that the host object's
    ''' overloads were dropped before resolution started.
    ''' </para>
    ''' </summary>
    <Fact>
    Public Sub HostObjectBinding_HostMembersDoNotJoinSubmissionMethodGroups()
        ' Positive partner: with no submission level 'Describe', the host object's String overload is reachable.
        AssertHostChainRuns(
            GetType(HostObjectFixtures.OverloadedMembers), New HostObjectFixtures.OverloadedMembers(), "S2",
            "? Describe(""2"")")

        ' The submission's own overload takes the same argument, and it is the narrowing conversion that makes the
        ' call legal at all - which is why the pinned value, not a diagnostic, is the discriminator here.
        AssertHostChainRuns(
            GetType(HostObjectFixtures.OverloadedMembers), New HostObjectFixtures.OverloadedMembers(), "D2",
            Lines("Function Describe(a As Double) As String",
                  "    Return ""D"" & a",
                  "End Function"),
            "? Describe(""2"")")

        AssertHostChainReports(
            GetType(HostObjectFixtures.OverloadedMembers), New HostObjectFixtures.OverloadedMembers(), "BC30311",
            Lines("Function Describe(a As Double) As String",
                  "    Return ""D"" & a",
                  "End Function"),
            "? Describe(New System.Text.StringBuilder(""a""))")
    End Sub

#End Region

#Region "cell 11-12: Shared context versus instance context"

    ''' <summary>
    ''' Cell 11: the C# pair <c>StaticMethodCannotAccessGlobalInstance</c>
    ''' (<c>InteractiveSessionTests.cs:1854</c>) and <c>StaticLocalFunctionCannotAccessGlobalInstance</c>
    ''' (<c>:1873</c>). VB has no local function, so the dual is a top level <c>Shared</c> method, and the second
    ''' half puts the same read inside a lambda declared by that <c>Shared</c> method - the closest VB shape to
    ''' the C# static local function.
    ''' <para>
    ''' Expected <c>BC30469</c> (<c>ERR_ObjectReferenceNotSupplied</c>, "Reference to a non-shared member requires
    ''' an object reference"), not <c>BC30451</c>: the host object member <em>is</em> found - it is the receiver
    ''' that is refused at <c>Binder_Expressions.vb:2616</c> (<c>Not currentMember.IsShared</c>), and
    ''' <c>CheckSharedSymbolAccess</c> then reports at <c>Binder_Expressions.vb:3755-3774</c>. C# reports
    ''' <c>CS0120</c> for the same situation.
    ''' </para>
    ''' </summary>
    <Fact>
    Public Sub HostObjectBinding_SharedContextCannotReadHostInstanceMember()
        AssertHostChainReports(
            GetType(HostObjectFixtures.InstanceValueMembers), New HostObjectFixtures.InstanceValueMembers(), "BC30469",
            Lines("Shared Function ReadValue() As Boolean",
                  "    Return Value",
                  "End Function"),
            "? ReadValue()")

        AssertHostChainReports(
            GetType(HostObjectFixtures.InstanceValueMembers), New HostObjectFixtures.InstanceValueMembers(), "BC30469",
            Lines("Shared Function ReadValue() As Boolean",
                  "    Dim read As System.Func(Of Boolean) = Function() Value",
                  "    Return read()",
                  "End Function"),
            "? ReadValue()")
    End Sub

    ''' <summary>
    ''' Cell 12: <c>LocalFunctionCanAccessGlobalInstance</c> (<c>InteractiveSessionTests.cs:1895</c>). The VB dual
    ''' of the C# non static local function is a top level non <c>Shared</c> method plus a top level lambda; both
    ''' read the host object's instance field and both must return the host object's value.
    ''' </summary>
    <Fact>
    Public Sub HostObjectBinding_InstanceContextCanReadHostInstanceMember()
        AssertHostChainRuns(
            GetType(HostObjectFixtures.InstanceValueMembers), New HostObjectFixtures.InstanceValueMembers(), True,
            Lines("Function ReadValue() As Boolean",
                  "    Return Value",
                  "End Function"),
            "? ReadValue()")

        AssertHostChainRuns(
            GetType(HostObjectFixtures.InstanceValueMembers), New HostObjectFixtures.InstanceValueMembers(), True,
            "Dim read As System.Func(Of Boolean) = Function() Value",
            "? read()")
    End Sub

#End Region

End Class

''' <summary>
''' <c>Namespace Global.&lt;name&gt;</c> keeps this type out of the project root namespace, which is the contrast
''' shape of the <c>HostObjectInRootNamespace</c> cell above.
''' </summary>
Namespace Global.GlobalQualifiedHostFixture

    ''' <summary>The global namespace dual of the C# <c>InteractiveFixtures_TopLevelHostObject</c> fixture.</summary>
    Public Class GlobalQualifiedHostObject
        Public X, Y, Z As Integer
    End Class

End Namespace

''' <summary>
''' The host object in the project root namespace: declared without a <c>Namespace</c> block, so the project's
''' <c>RootNamespace</c> is prepended to its metadata name.
''' </summary>
Public Class ProjectRootNamespaceHostObject
    Public X, Y, Z As Integer
End Class

''' <summary>
''' C# <c>InteractiveSessionTests.M&lt;T&gt;</c> (<c>InteractiveSessionTests.cs:1519</c>). <c>TypeArgumentName</c> is
''' added to the C# fixture shape on purpose: it is what lets the cells assert that the type argument reached this
''' type's own type parameter instead of only asserting the absence of a diagnostic.
''' </summary>
Public Class HostObjectGenericMembers(Of T)

    Private Function Hidden() As Integer
        Return 3
    End Function

    Public Function G() As T
        Return Nothing
    End Function

    Public Function TypeArgumentName() As String
        Return GetType(T).Name
    End Function

End Class

''' <summary>
''' The generic class of the cell above, closed by inheritance instead of by a type argument on
''' <c>globalsType</c>. Its metadata name is an ordinary one, so it is the control of that cell.
''' </summary>
Public Class HostObjectClosedGenericMembers
    Inherits HostObjectGenericMembers(Of String)
End Class

''' <summary>
''' The <c>Outer.GenericMembers(Of T)</c> shape of the second symptom of issue 22: a generic type nested in a non
''' generic one. Its <c>FullName</c> carries the reflection nesting separator and the type argument form at once,
''' which is what used to route the name based lookup into the nested type walk.
''' </summary>
Public Class HostObjectNestedMembers

    Public Class NestedGenericMembers(Of T)

        Public Function G() As T
            Return Nothing
        End Function

        Public Function TypeArgumentName() As String
            Return GetType(T).Name
        End Function

    End Class

End Class

''' <summary>
''' A generic enclosing type. Reflection reports it as the generic type <em>definition</em> when a nested type
''' reports its <c>DeclaringType</c>, so the nested types below are the shapes that need the enclosing type closed
''' with the arguments the original type carries for it.
''' </summary>
Public Class HostObjectGenericOuter(Of T)

    Public Class NestedPlainMembers

        Public Function G() As Integer
            Return 1
        End Function

        Public Function OuterTypeArgumentName() As String
            Return GetType(T).Name
        End Function

    End Class

    Public Class NestedGenericMembers(Of U)

        Public Function G() As U
            Return Nothing
        End Function

        Public Function OwnTypeArgumentName() As String
            Return GetType(U).Name
        End Function

        Public Function OuterTypeArgumentName() As String
            Return GetType(T).Name
        End Function

    End Class

End Class

''' <summary>
''' The nesting depth of <c>TypeResolutionTests.TypeSymbolFromReflectionType</c>
''' (<c>Compilers\CSharp\Test\Symbol\Symbols\TypeResolutionTests.cs:102-111,142</c>): a non generic type inside a
''' generic type inside a generic type, with a type argument at three different levels.
''' </summary>
Public Class HostObjectDeepOuter(Of S, T)

    Public Class Middle

        Public Class Inner(Of U, V)

            Public Class Leaf(Of W)

                Public Function Arguments() As String
                    Dim names = New String() {GetType(S).Name, GetType(T).Name, GetType(U).Name, GetType(V).Name, GetType(W).Name}
                    Return String.Join(",", names)
                End Function

            End Class

        End Class

    End Class

End Class

''' <summary>
''' The VB host objects of the U3 cells. They are ordinary types of this test project - the C# cells nest theirs
''' in the test class (<c>InteractiveSessionTests.cs:1493-1525</c>), and the only reason the ones below sit in a
''' separate type is that a <c>Private</c> nested class needs a reachable factory.
''' </summary>
Public NotInheritable Class HostObjectFixtures

    Private Sub New()
    End Sub

    ''' <summary>C# <c>InteractiveSessionTests.B</c> (<c>InteractiveSessionTests.cs:1493</c>).</summary>
    Public Class BaseMembers
        Public x As Integer = 1
        Public w As Integer = 4
    End Class

    ''' <summary>C# <c>InteractiveSessionTests.I</c> (<c>InteractiveSessionTests.cs:1507</c>).</summary>
    Public Interface IMembers
        Property N As String
        Function Z() As Integer
    End Interface

    ''' <summary>C# <c>InteractiveSessionTests.C</c> (<c>InteractiveSessionTests.cs:1498</c>).</summary>
    Public Class PublicMembers
        Inherits BaseMembers
        Implements IMembers

        Public Shared ReadOnly StaticField As Integer = 123
        Public ReadOnly Property Y As Integer = 2
        Public Property N As String = "2" Implements IMembers.N

        Public Function Z() As Integer Implements IMembers.Z
            Return 3
        End Function

        Public Overrides Function GetHashCode() As Integer
            Return 123
        End Function
    End Class

    ''' <summary>
    ''' The private member case: C# reaches it through <c>M&lt;int&gt;</c>'s private <c>F()</c>
    ''' (<c>InteractiveSessionTests.cs:1522,1580</c>); the non generic type here keeps the cell independent of the
    ''' generic host type divergence documented on <c>HostObjectBinding_PublicGenericClassMembers</c>.
    ''' </summary>
    Public Class PublicMembersWithPrivateFunction
        Private Function Hidden() As Integer
            Return 3
        End Function

        Public Function Z() As Integer
            Return 3
        End Function
    End Class

    ''' <summary>C# <c>InteractiveSessionTests.PrivateClass</c> (<c>InteractiveSessionTests.cs:1513</c>).</summary>
    Private Class HiddenMembers
        Implements IMembers

        Public Property N As String Implements IMembers.N

        Public Function Z() As Integer Implements IMembers.Z
            Return 3
        End Function
    End Class

    ''' <summary>
    ''' Host object with its own overload set (cells 8 and 10). The <c>StringBuilder</c> overload is the one cell 10
    ''' uses to force a diagnostic: it has no conversion from or to <c>Double</c>, so once the host object is out of
    ''' the candidate set there is no way for the submission's <c>Double</c> overload to accept the argument.
    ''' </summary>
    Public Class OverloadedMembers
        Public Function Describe(a As Integer) As String
            Return "I" & a
        End Function

        Public Function Describe(a As String) As String
            Return "S" & a
        End Function

        Public Function Describe(a As System.Text.StringBuilder) As String
            Return "B" & a.ToString()
        End Function
    End Class

    ''' <summary>C# <c>InteractiveSessionTests.F</c> (<c>InteractiveSessionTests.cs:1849</c>).</summary>
    Public Class InstanceValueMembers
        Public Value As Boolean = True
    End Class

    ' A Private nested class is not nameable from the test class, so the type object and an instance are exposed
    ' instead. GetType is the only way to hand the type to globalsType.

    Friend Shared ReadOnly Property HiddenMembersType As Type
        Get
            Return GetType(HiddenMembers)
        End Get
    End Property

    Friend Shared Function CreateHiddenMembers() As Object
        Return New HiddenMembers()
    End Function

End Class
