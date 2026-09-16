' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System
Imports System.Linq
Imports System.Linq.Expressions
Imports System.Xml.Linq
Imports Microsoft.CodeAnalysis.Scripting
Imports Xunit

''' <summary>
''' Query and XML literal cells of the syntax ledger (test-plan §C.3.C). Both families are expression syntax, and
''' the top level of a script is the one container an ordinary compilation cannot reach: the submission class has
''' no project file and no base type, so the same text means something else there. Every cell below writes the
''' construct as a top level statement or a top level field and pins what that container does with it - the query
''' runs and yields a value, or the construct is rejected with a named diagnostic.
''' <para>
''' The declaration in every query cell carries an explicit <c>As</c> clause on purpose. A top level <c>Dim</c>
''' without one is bound as <c>Object</c> (design-overview.md §3, pinned by
''' <c>ScriptModeTopLevelInferenceTests.TopLevelQueryField_IsObject</c>), and an <c>Object</c> field cannot carry
''' the member calls these cells need. Where the projection is anonymous and therefore has no name to write, the
''' field is typed as the untyped sequence and the elements are read through it.
''' </para>
''' <para>
''' <see cref="ScriptModeConformance.DefaultOptions"/> references neither System.Linq nor System.Xml.Linq, so the
''' two option sets below add them. Both come from assemblies already on disk which the metadata layer opens
''' read-only; nothing is written, no process is started, and there is no registry or network access.
''' </para>
''' </summary>
Public Class ScriptModeQueryAndXmlConformanceTests

    ''' <summary>
    ''' The query constructors (<c>Select</c>/<c>Where</c>/<c>GroupBy</c>) live in System.Linq, <c>IQueryable</c>
    ''' and the query's <c>Expression(Of T)</c> overloads in System.Linq.Queryable / System.Linq.Expressions.
    ''' Without the references a query reports BC36593 and nothing else.
    ''' </summary>
    Private Shared ReadOnly QueryOptions As ScriptOptions =
        ScriptModeConformance.DefaultOptions.
            AddReferences(GetType(Enumerable).Assembly).
            AddReferences(GetType(Queryable).Assembly).
            AddReferences(GetType(Expression).Assembly).
            AddImports("System.Linq")

    ''' <summary>XML literals are typed by <c>System.Xml.Linq</c>, which the scripting defaults do not reference.</summary>
    Private Shared ReadOnly XmlOptions As ScriptOptions =
        QueryOptions.AddReferences(GetType(XElement).Assembly)

#Region "Query Expressions"

    ''' <summary>
    ''' The root cell: a query expression written at the top level of the script, from the <c>From</c> operator to
    ''' the final <c>Select</c>, with the whole result pinned. The <c>As</c> clause is what keeps the field from
    ''' being an <c>Object</c>.
    ''' </summary>
    <Fact>
    Public Sub TopLevelQueryExpression_Conforms()
        ScriptModeConformance.AssertRuns(
            "Imports System.Linq" & vbCrLf &
            "Dim nums As Integer() = {3, 1, 2}" & vbCrLf &
            "Dim sorted As System.Collections.Generic.IEnumerable(Of Integer) = From n In nums Order By n Select n" & vbCrLf &
            "Return String.Join("","", sorted)", "1,2,3", QueryOptions)
    End Sub

#End Region

#Region "Range Variables"

    ''' <summary>
    ''' Two range variables introduced by one <c>From</c> clause, the outer one read inside the <c>Where</c> that
    ''' governs both and the inner one read by the projection. The order of the result is the order the range
    ''' variables were introduced in, so a compiler that dropped or renamed either variable answers differently.
    ''' </summary>
    <Fact>
    Public Sub TopLevelRangeVariables_Conform()
        ScriptModeConformance.AssertRuns(
            "Imports System.Linq" & vbCrLf &
            "Dim nums As Integer() = {1, 2, 3}" & vbCrLf &
            "Dim letters As String() = {""a"", ""b""}" & vbCrLf &
            "Dim pairs As System.Collections.Generic.IEnumerable(Of String) = From n In nums, s In letters Where n > 1 Select s & n" & vbCrLf &
            "Return String.Join("","", pairs)", "a2,b2,a3,b3", QueryOptions)
    End Sub

#End Region

#Region "Queryable Types"

    ''' <summary>
    ''' The spec allows the query operators to be translated into methods whose delegate parameter is
    ''' <c>Expression(Of D)</c>, which is what makes an <c>IQueryable(Of T)</c> source queryable. The same
    ''' <c>Select</c> text therefore has to resolve to <c>Queryable.Select</c> here and to <c>Enumerable.Select</c>
    ''' in the other cells of this region.
    ''' </summary>
    <Fact>
    Public Sub TopLevelQueryOverQueryableSource_Conforms()
        ScriptModeConformance.AssertRuns(
            "Imports System.Linq" & vbCrLf &
            "Imports System.Linq.Expressions" & vbCrLf &
            "Dim nums As Integer() = {1, 2, 3}" & vbCrLf &
            "Dim queryable As System.Linq.IQueryable(Of Integer) = System.Linq.Queryable.AsQueryable(nums)" & vbCrLf &
            "Dim doubled As System.Linq.IQueryable(Of Integer) = From n In queryable Select n * 2" & vbCrLf &
            "Return System.Linq.Enumerable.Sum(doubled)", 12, QueryOptions)
    End Sub

    ''' <summary>
    ''' Negative cell: a source that is not a collection at all is rejected. BC36593 is the diagnostic the query
    ''' binder reports when no <c>Select</c> can be found for the source type; the positive cell above and the
    ''' other query cells run on the very same option set, so nothing but the source type can account for it.
    ''' </summary>
    <Fact>
    Public Sub TopLevelQueryOverNonQueryableSource_IsReported()
        ScriptModeConformance.AssertReports(
            "Imports System.Linq" & vbCrLf &
            "Dim bad As System.Collections.Generic.IEnumerable(Of Integer) = From n In 5 Select n" & vbCrLf &
            "Return bad", "BC36593", QueryOptions)
    End Sub

#End Region

#Region "Default Query Indexer"

    ''' <summary>
    ''' A queryable collection type has no default property of its own, so the spec gives it one over
    ''' <c>ElementAtOrDefault</c>. The receiver is the query result typed as the untyped sequence, not the array,
    ''' so the second element can only come from that synthesized indexer - and the out of range read answers
    ''' <c>0</c>, which an indexer over the array could not do at all.
    ''' </summary>
    <Fact>
    Public Sub TopLevelDefaultQueryIndexer_Conforms()
        ScriptModeConformance.AssertRuns(
            "Imports System.Linq" & vbCrLf &
            "Dim nums As Integer() = {10, 20, 30}" & vbCrLf &
            "Dim view As System.Collections.Generic.IEnumerable(Of Integer) = From n In nums Select n" & vbCrLf &
            "Return view(1) & ""/"" & view(5)", "20/0", QueryOptions)
    End Sub

#End Region

#Region "From Query Operator"

    ''' <summary>
    ''' The chained form of the operator: a second <c>From</c> clause whose source is the same collection. The
    ''' result is the cross product in range variable order, which a single <c>From</c> clause could not produce.
    ''' </summary>
    <Fact>
    Public Sub TopLevelFromQueryOperator_Conforms()
        ScriptModeConformance.AssertRuns(
            "Imports System.Linq" & vbCrLf &
            "Dim nums As Integer() = {1, 2}" & vbCrLf &
            "Dim letters As String() = {""a"", ""b""}" & vbCrLf &
            "Dim pairs As System.Collections.Generic.IEnumerable(Of String) = From n In nums From s In letters Select s & n" & vbCrLf &
            "Return String.Join("","", pairs)", "a1,b1,a2,b2", QueryOptions)
    End Sub

#End Region

#Region "Join Query Operator"

    ''' <summary>
    ''' The <c>Join</c> operator matches the outer range variable against the joined one with <c>Equals</c>, and
    ''' keeps only the matches: three keys against three values share two, so the result is two rows rather than
    ''' the nine a cross product would give.
    ''' </summary>
    <Fact>
    Public Sub TopLevelJoinQueryOperator_Conforms()
        ScriptModeConformance.AssertRuns(
            "Imports System.Linq" & vbCrLf &
            "Dim keys As String() = {""a"", ""b"", ""c""}" & vbCrLf &
            "Dim vals As String() = {""b"", ""c"", ""d""}" & vbCrLf &
            "Dim matched As System.Collections.Generic.IEnumerable(Of String) = From k In keys Join v In vals On k Equals v Select k & v" & vbCrLf &
            "Return String.Join("","", matched)", "bb,cc", QueryOptions)
    End Sub

#End Region

#Region "Let Query Operator"

    ''' <summary>
    ''' <c>Let</c> introduces a computed range variable rather than a new source, and the following <c>Where</c>
    ''' and <c>Select</c> both read it. A compiler that only introduced range variables for <c>From</c> could not
    ''' bind the name at all.
    ''' </summary>
    <Fact>
    Public Sub TopLevelLetQueryOperator_Conforms()
        ScriptModeConformance.AssertRuns(
            "Imports System.Linq" & vbCrLf &
            "Dim nums As Integer() = {1, 2, 3, 4}" & vbCrLf &
            "Dim big As System.Collections.Generic.IEnumerable(Of Integer) = From n In nums Let doubled = n * 2 Where doubled > 4 Select doubled" & vbCrLf &
            "Return String.Join("","", big)", "6,8", QueryOptions)
    End Sub

#End Region

#Region "Select Query Operator"

    ''' <summary>
    ''' The named projection form, which introduces two members at once. The projection is anonymous, so the field
    ''' is typed as the untyped sequence and the members are read back through it.
    ''' </summary>
    <Fact>
    Public Sub TopLevelSelectQueryOperator_Conforms()
        ScriptModeConformance.AssertRuns(
            "Imports System.Linq" & vbCrLf &
            "Dim nums As Integer() = {1, 2}" & vbCrLf &
            "Dim projected As System.Collections.IEnumerable = From n In nums Select Doubled = n * 2, Tripled = n * 3" & vbCrLf &
            "Return String.Join("","", projected.Cast(Of Object)().Select(Function(x) x.Doubled & "":"" & x.Tripled))",
            "2:3,4:6", QueryOptions)
    End Sub

#End Region

#Region "Distinct Query Operator"

    ''' <summary>
    ''' <c>Distinct</c> on a projection with repeated values. The input is deliberately out of order, so the
    ''' expected reading is first appearance order: a de-duplication that sorted on the way out answers
    ''' <c>1,2,3</c> and is caught.
    ''' </summary>
    <Fact>
    Public Sub TopLevelDistinctQueryOperator_Conforms()
        ScriptModeConformance.AssertRuns(
            "Imports System.Linq" & vbCrLf &
            "Dim nums As Integer() = {3, 1, 2, 2, 1, 3}" & vbCrLf &
            "Dim distinct As System.Collections.Generic.IEnumerable(Of Integer) = From n In nums Select n Distinct" & vbCrLf &
            "Return String.Join("","", distinct)", "3,1,2", QueryOptions)
    End Sub

#End Region

#Region "Where Query Operator"

    ''' <summary>The predicate form of <c>Where</c>, reading the range variable of the <c>From</c> clause.</summary>
    <Fact>
    Public Sub TopLevelWhereQueryOperator_Conforms()
        ScriptModeConformance.AssertRuns(
            "Imports System.Linq" & vbCrLf &
            "Dim nums As Integer() = {1, 2, 3, 4, 5}" & vbCrLf &
            "Dim even As System.Collections.Generic.IEnumerable(Of Integer) = From n In nums Where n Mod 2 = 0 Select n" & vbCrLf &
            "Return String.Join("","", even)", "2,4", QueryOptions)
    End Sub

#End Region

#Region "Partition Query Operators"

    ''' <summary>
    ''' <c>Skip</c> and <c>Take</c> in one query: the window starts at the second element and is three wide, so
    ''' both bounds have to be honored for the answer to be right.
    ''' </summary>
    <Fact>
    Public Sub TopLevelPartitionQueryOperators_Conform()
        ScriptModeConformance.AssertRuns(
            "Imports System.Linq" & vbCrLf &
            "Dim nums As Integer() = {1, 2, 3, 4, 5}" & vbCrLf &
            "Dim page As System.Collections.Generic.IEnumerable(Of Integer) = From n In nums Skip 1 Take 3 Select n" & vbCrLf &
            "Return String.Join("","", page)", "2,3,4", QueryOptions)
    End Sub

#End Region

#Region "Order By Query Operator"

    ''' <summary>
    ''' Two ordering keys, the second descending: the primary key alone cannot produce the pinned order, and
    ''' neither can a compiler that ignored the <c>Descending</c> modifier.
    ''' </summary>
    <Fact>
    Public Sub TopLevelOrderByQueryOperator_Conforms()
        ScriptModeConformance.AssertRuns(
            "Imports System.Linq" & vbCrLf &
            "Dim nums As Integer() = {4, 2, 1, 3}" & vbCrLf &
            "Dim ordered As System.Collections.Generic.IEnumerable(Of Integer) = From n In nums Order By n Mod 3, n Descending Select n" & vbCrLf &
            "Return String.Join("","", ordered)", "3,4,1,2", QueryOptions)
    End Sub

#End Region

#Region "Group By Query Operator"

    ''' <summary>
    ''' <c>By</c> with an <c>Into</c> group, then a projection that counts it. The expected order is the order the
    ''' groups were created in, and the two counts differ, so both the grouping and the <c>Into</c> binding are
    ''' pinned.
    ''' </summary>
    <Fact>
    Public Sub TopLevelGroupByQueryOperator_Conforms()
        ScriptModeConformance.AssertRuns(
            "Imports System.Linq" & vbCrLf &
            "Dim nums As Integer() = {1, 2, 3, 4, 5}" & vbCrLf &
            "Dim grouped As System.Collections.IEnumerable = From n In nums Group By parity = n Mod 2 Into g = Group Select parity, c = g.Count()" & vbCrLf &
            "Return String.Join("","", grouped.Cast(Of Object)().Select(Function(x) x.parity & "":"" & x.c))",
            "1:3,0:2", QueryOptions)
    End Sub

#End Region

#Region "Aggregate Query Operator"

    ''' <summary>
    ''' <c>Aggregate</c> starts the query like <c>From</c> does, but its <c>Into</c> clause reduces the sequence to
    ''' a single value instead of a sequence, so the field is typed <c>Integer</c> and the sum is read directly.
    ''' </summary>
    <Fact>
    Public Sub TopLevelAggregateQueryOperator_Conforms()
        ScriptModeConformance.AssertRuns(
            "Imports System.Linq" & vbCrLf &
            "Dim nums As Integer() = {1, 2, 3, 4}" & vbCrLf &
            "Dim total As Integer = Aggregate n In nums Into Sum()" & vbCrLf &
            "Return total", 10, QueryOptions)
    End Sub

#End Region

#Region "Group Join Query Operator"

    ''' <summary>
    ''' <c>Group Join</c> keeps every outer element and groups the matches beside it, so the outer keys count is
    ''' the row count: "a" matches twice, "b" once, and a key with no match would still be a row with a count of
    ''' zero.
    ''' </summary>
    <Fact>
    Public Sub TopLevelGroupJoinQueryOperator_Conforms()
        ScriptModeConformance.AssertRuns(
            "Imports System.Linq" & vbCrLf &
            "Dim keys As String() = {""a"", ""b""}" & vbCrLf &
            "Dim vals As String() = {""a"", ""a"", ""b""}" & vbCrLf &
            "Dim grouped As System.Collections.IEnumerable = From k In keys Group Join v In vals On k Equals v Into g = Group Select k, c = g.Count()" & vbCrLf &
            "Return String.Join("","", grouped.Cast(Of Object)().Select(Function(x) x.k & "":"" & x.c))",
            "a:2,b:1", QueryOptions)
    End Sub

#End Region

#Region "Conditional Expressions"

    ''' <summary>
    ''' Both arities of the conditional expression at the top level, plus the half that separates it from the
    ''' <c>IIf</c> runtime function: the untaken branch is a call that throws, so an implementation that evaluated
    ''' both operands would fail the run rather than answer <c>1</c>. The two operand form is read through a
    ''' <c>Nothing</c> reference and through a boolean test.
    ''' </summary>
    <Fact>
    Public Sub TopLevelConditionalExpression_Conforms()
        ScriptModeConformance.AssertRuns(
            "Function Boom() As Integer" & vbCrLf &
            "    Throw New System.Exception(""untaken branch was evaluated"")" & vbCrLf &
            "End Function" & vbCrLf &
            "Dim guard As Integer = 0" & vbCrLf &
            "Dim picked As Integer = If(guard = 0, 1, Boom())" & vbCrLf &
            "Dim name As String = Nothing" & vbCrLf &
            "Dim fallback As String = ""unknown""" & vbCrLf &
            "Dim two As String = If(name, fallback)" & vbCrLf &
            "Dim three As String = If(name Is Nothing, ""none"", ""some"")" & vbCrLf &
            "Return picked & ""/"" & two & ""/"" & three", "1/unknown/none")
    End Sub

#End Region

#Region "XML Literal Expressions"

    ''' <summary>
    ''' The root of the XML family: an element literal with an attribute and text content, declared as a top level
    ''' field of the submission class. The value is read back through the LINQ to XML object model, so the literal
    ''' really produced an <c>XElement</c>.
    ''' </summary>
    <Fact>
    Public Sub TopLevelXmlElementLiteral_Conforms()
        ScriptModeConformance.AssertRuns(
            "Dim element As System.Xml.Linq.XElement = <book title=""t"">body</book>" & vbCrLf &
            "Return element.Name.LocalName & "":"" & element.Attribute(""title"").Value & "":"" & element.Value",
            "book:t:body", XmlOptions)
    End Sub

#End Region

#Region "XML Lexical Rules"

    ''' <summary>
    ''' The three lexical productions of the section - <c>XMLCharacter</c>, <c>XMLString</c> and
    ''' <c>XMLWhitespace</c> - carried by the only construct that has them: the text inside an XML literal. The
    ''' whitespace and line terminators that separate the child elements are <c>XMLWhitespace</c> and never become
    ''' text (the parent's value has only the two digits), while a character reference and a tab inside a text node
    ''' are <c>XMLCharacter</c>s that survive verbatim. Both readings are lengths and identifiers only, so no
    ''' expected value depends on the culture.
    ''' </summary>
    <Fact>
    Public Sub TopLevelXmlLiteralLexicalRules_Conform()
        ScriptModeConformance.AssertRuns(
            "Dim element As System.Xml.Linq.XElement = <root>" & vbCrLf &
            "    <a>1</a>" & vbCrLf &
            "    <b>2</b>" & vbCrLf &
            "</root>" & vbCrLf &
            "Dim text As System.Xml.Linq.XElement = <t>A&#66;C&#9;D</t>" & vbCrLf &
            "Return element.Elements().Count() & ""/"" & element.Value & ""/"" & text.Value.Length & ""/"" & text.Value.StartsWith(""ABC"")",
            "2/12/5/True", XmlOptions)
    End Sub

#End Region

#Region "Embedded Expressions"

    ''' <summary>
    ''' <c>&lt;%= %&gt;</c> holes in the content and in an attribute value, both filled by top level fields. The
    ''' attribute hole is the one that cannot be written as a plain expression, since the literal's attribute
    ''' syntax has no place for a value expression.
    ''' </summary>
    <Fact>
    Public Sub TopLevelXmlEmbeddedExpressions_Conform()
        ScriptModeConformance.AssertRuns(
            "Dim inner As String = ""text""" & vbCrLf &
            "Dim count As Integer = 3" & vbCrLf &
            "Dim element As System.Xml.Linq.XElement = <root attr=<%= inner %>><%= inner %>-<%= count %></root>" & vbCrLf &
            "Return element.Value & ""/"" & element.Attribute(""attr"").Value", "text-3/text", XmlOptions)
    End Sub

#End Region

#Region "XML Documents"

    ''' <summary>
    ''' A document literal: the XML declaration followed by the root element, typed as <c>XDocument</c>. The
    ''' declaration survives into the object model, which is what separates the document production from a bare
    ''' element.
    ''' </summary>
    <Fact>
    Public Sub TopLevelXmlDocumentLiteral_Conforms()
        ScriptModeConformance.AssertRuns(
            "Dim doc As System.Xml.Linq.XDocument = <?xml version=""1.0"" encoding=""utf-8""?>" & vbCrLf &
            "<root><a/></root>" & vbCrLf &
            "Return doc.Root.Name.LocalName & ""/"" & doc.Declaration.Version", "root/1.0", XmlOptions)
    End Sub

#End Region

#Region "XML Elements"

    ''' <summary>
    ''' The element production in its three shapes at once: a parent with an attribute, a child with content and an
    ''' empty child. The child count and the parent's text value are the two readings a compiler that dropped an
    ''' empty element or an attribute would get wrong.
    ''' </summary>
    <Fact>
    Public Sub TopLevelXmlElementShapes_Conform()
        ScriptModeConformance.AssertRuns(
            "Imports System.Linq" & vbCrLf &
            "Dim element As System.Xml.Linq.XElement = <outer attr=""v""><inner>1</inner><empty/></outer>" & vbCrLf &
            "Return element.Name.LocalName & ""/"" & element.Attribute(""attr"").Value & ""/"" & element.Elements().Count() & ""/"" & element.Value",
            "outer/v/2/1", XmlOptions)
    End Sub

#End Region

#Region "XML Namespaces"

    ''' <summary>
    ''' An <c>xmlns</c> prefixed attribute inside the element literal. This is the namespace of the literal itself,
    ''' not the <c>Imports &lt;xmlns:...&gt;</c> import clause of §C.3.H: the ranged name and the child resolved
    ''' through <c>XName.Get(name, namespace)</c> both have to carry the namespace for the reading to be right.
    ''' </summary>
    <Fact>
    Public Sub TopLevelXmlLiteralNamespaces_Conform()
        ScriptModeConformance.AssertRuns(
            "Dim element As System.Xml.Linq.XElement = <p:root xmlns:p=""urn:test""><p:child>v</p:child></p:root>" & vbCrLf &
            "Dim child As System.Xml.Linq.XElement = element.Element(System.Xml.Linq.XName.Get(""child"", ""urn:test""))" & vbCrLf &
            "Return element.Name.NamespaceName & ""/"" & element.Name.LocalName & ""/"" & child.Value",
            "urn:test/root/v", XmlOptions)
    End Sub

#End Region

#Region "XML Processing Instructions"

    ''' <summary>
    ''' The processing instruction production in both positions it can take: as the literal of its own and inside
    ''' the content of an element. The node count of the parent shows the instruction became a node rather than
    ''' being dropped as markup.
    ''' </summary>
    <Fact>
    Public Sub TopLevelXmlProcessingInstructionLiterals_Conform()
        ScriptModeConformance.AssertRuns(
            "Imports System.Linq" & vbCrLf &
            "Dim pi As System.Xml.Linq.XProcessingInstruction = <?target some data?>" & vbCrLf &
            "Dim element As System.Xml.Linq.XElement = <root><?mark inside?></root>" & vbCrLf &
            "Return pi.Target & "":"" & pi.Data & ""/"" & element.Nodes().Count()",
            "target:some data/1", XmlOptions)
    End Sub

#End Region

#Region "XML Comments"

    ''' <summary>
    ''' The comment production, standalone and inside an element. The text around the comment is part of its value,
    ''' so the pinned string is the literal content with the spaces the literal carries.
    ''' </summary>
    <Fact>
    Public Sub TopLevelXmlCommentLiterals_Conform()
        ScriptModeConformance.AssertRuns(
            "Imports System.Linq" & vbCrLf &
            "Dim comment As System.Xml.Linq.XComment = <!-- hello -->" & vbCrLf &
            "Dim element As System.Xml.Linq.XElement = <root><!-- inner --></root>" & vbCrLf &
            "Return comment.Value & ""/"" & element.Nodes().Count()",
            " hello /1", XmlOptions)
    End Sub

#End Region

#Region "CDATA Sections"

    ''' <summary>
    ''' The CDATA production, whose whole point is that markup characters inside it are text. The reading spans both
    ''' angles and the element's value, which can only come out that way if the section was recognized as CDATA
    ''' rather than parsed as markup.
    ''' </summary>
    <Fact>
    Public Sub TopLevelXmlCDataLiterals_Conform()
        ScriptModeConformance.AssertRuns(
            "Dim cdata As System.Xml.Linq.XCData = <![CDATA[<not markup>]]>" & vbCrLf &
            "Dim element As System.Xml.Linq.XElement = <root><![CDATA[a<b]]></root>" & vbCrLf &
            "Return cdata.Value & ""/"" & element.Value",
            "<not markup>/a<b", XmlOptions)
    End Sub

#End Region

#Region "XML Member Access Expressions"

    ''' <summary>
    ''' The three axes at the top level: <c>.<name></c> over the child elements, <c>.@name</c> over the
    ''' attributes and <c>...&lt;name&gt;</c> over the descendants. The direct axis finds one item and the
    ''' descendant axis finds two, so a compiler that mapped both onto the same axis cannot produce the reading,
    ''' and the attribute axis is the only source of <c>"1"</c>.
    ''' </summary>
    <Fact>
    Public Sub TopLevelXmlMemberAccessAxes_Conform()
        ScriptModeConformance.AssertRuns(
            "Imports System.Linq" & vbCrLf &
            "Dim element As System.Xml.Linq.XElement = <root><item id=""1"">a</item><group><item id=""2"">b</item></group></root>" & vbCrLf &
            "Dim direct As System.Collections.Generic.IEnumerable(Of System.Xml.Linq.XElement) = element.<item>" & vbCrLf &
            "Dim deep As System.Collections.Generic.IEnumerable(Of System.Xml.Linq.XElement) = element...<item>" & vbCrLf &
            "Return direct.Count() & ""/"" & direct(0).@id & ""/"" & element.<item>.Value & ""/"" & deep.Count()",
            "1/1/a/2", XmlOptions)
    End Sub

#End Region

End Class
