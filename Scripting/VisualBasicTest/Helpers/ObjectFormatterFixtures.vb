Imports System.Reflection
Imports System.Runtime.CompilerServices

Namespace Global.ObjectFormatterFixtures
    Friend Class Outer
        Public Class Nested(Of T)
            Public ReadOnly A As Integer = 1
            Public ReadOnly B As Integer = 2
            Public Const S As Integer = 3
        End Class
    End Class

    Friend Class A(Of T)
        Public Class B(Of S)
            Public Class C
                Public Class D(Of Q, R)
                    Public Class E
                    End Class
                End Class
            End Class
        End Class

        Public Shared ReadOnly X As New B(Of T)
    End Class

    Friend Class Sort
        Public ReadOnly ab As Byte = 1
        Public ReadOnly aB2 As SByte = -1
        Public ReadOnly Ac As Short = -1
        Public ReadOnly Ad As UShort = 1
        Public ReadOnly ad2 As Integer = -1
        Public ReadOnly aE As UInteger = 1
        Public ReadOnly aF As Long = -1
        Public ReadOnly AG As ULong = 1
    End Class

    Friend Class Signatures
        Public Shared ReadOnly Arrays As MethodInfo = GetType(Signatures).GetMethod(NameOf(ArrayParameters))
        Public Sub ArrayParameters(arrayOne() As Integer, arrayTwo(,) As Integer, arrayThree(,,) As Integer)
        End Sub
    End Class

    Friend Class RecursiveRootHidden
        Public ReadOnly A As Integer
        Public ReadOnly B As Integer

        <DebuggerBrowsable(DebuggerBrowsableState.RootHidden)>
        Public C As RecursiveRootHidden
    End Class

    Friend Class RecursiveProxy
        Private Class Proxy
            Public Sub New()
            End Sub
            Public Sub New(node As Node)
                x = node.value
                y = node.next
            End Sub

            Public ReadOnly x As Integer
            Public ReadOnly y As Node
        End Class

        <DebuggerTypeProxy(GetType(Proxy))>
        Public Class Node
            Public Sub New(value As Integer)
                If value < 5 Then
                    [next] = New Node(value + 1)
                End If
                Me.value = value
            End Sub

            Public ReadOnly value As Integer
            Public ReadOnly [next] As Node
        End Class
    End Class

    Friend Class InvalidRecursiveProxy
        Private Class Proxy
            Public Sub New()
            End Sub
            Public Sub New(c As Node)
            End Sub

            Public ReadOnly x As Integer
            Public ReadOnly p As Node = New Node
            Public ReadOnly y As Integer
        End Class

        <DebuggerTypeProxy(GetType(Proxy))>
        Public Class Node
            Public ReadOnly a As Integer
            Public ReadOnly b As Integer
        End Class
    End Class

    Friend Class ComplexProxyBase
        Private Function Goo() As Integer
            Return 1
        End Function
    End Class

    Friend Class ComplexProxy
        Inherits ComplexProxyBase

        Public Sub New()
        End Sub

        Public Sub New(b As Object)
        End Sub

        <DebuggerDisplay("*1")>
        Public ReadOnly Property _02_public_property_dd As Integer
            Get
                Return 1
            End Get
        End Property

        <DebuggerDisplay("*2")>
        Private ReadOnly Property _03_private_property_dd As Integer
            Get
                Return 1
            End Get
        End Property

        <DebuggerDisplay("*3")>
        Protected ReadOnly Property _04_protected_property_dd As Integer
            Get
                Return 1
            End Get
        End Property

        <DebuggerDisplay("*4")>
        Friend ReadOnly Property _05_internal_property_dd As Integer
            Get
                Return 1
            End Get
        End Property

        <DebuggerDisplay("+1")>
        <DebuggerBrowsable(DebuggerBrowsableState.Never)>
        Public ReadOnly _06_public_field_dd_never As Integer

        <DebuggerDisplay("+2")>
        Private ReadOnly _07_private_field_dd As Integer

        <DebuggerDisplay("+3")>
        Protected ReadOnly _08_protected_field_dd As Integer

        <DebuggerDisplay("+4")>
        Friend ReadOnly _09_internal_field_dd As Integer

        <DebuggerBrowsable(DebuggerBrowsableState.Collapsed)>
        Private ReadOnly _10_private_collapsed As Integer

        <DebuggerBrowsable(DebuggerBrowsableState.RootHidden)>
        Private ReadOnly _10_private_rootHidden As Integer

        Public ReadOnly _12_public As Integer
        Private ReadOnly _13_private As Integer
        Protected ReadOnly _14_protected As Integer
        Friend ReadOnly _15_internal As Integer

        <DebuggerDisplay("==" & vbCrLf & "=" & vbCrLf & "=")>
        Public ReadOnly _16_eolns As Integer

        <DebuggerDisplay("=={==")>
        Public ReadOnly _17_braces_0 As Integer

        <DebuggerDisplay("=={{==")>
        Public ReadOnly _17_braces_1 As Integer

        <DebuggerDisplay("=={'{'}==")>
        Public ReadOnly _17_braces_2 As Integer

        <DebuggerDisplay("=={'\{'}==")>
        Public ReadOnly _17_braces_3 As Integer

        <DebuggerDisplay("=={1/*{*/}==")>
        Public ReadOnly _17_braces_4 As Integer

        <DebuggerDisplay("=={'{'/*\}*/}==")>
        Public ReadOnly _17_braces_5 As Integer

        <DebuggerDisplay("=={'{'/*}*/}==")>
        Public ReadOnly _17_braces_6 As Integer

        <DebuggerDisplay("==\{\x\t==")>
        Public ReadOnly _19_escapes As Integer

        <DebuggerDisplay("{1+1}")>
        Public ReadOnly _21 As Integer

        <DebuggerDisplay("{""xxx""}")>
        Public ReadOnly _22 As Integer

        <DebuggerDisplay("{""xxx"",nq}")>
        Public ReadOnly _23 As Integer

        <DebuggerDisplay("{'x'}")>
        Public ReadOnly _24 As Integer

        <DebuggerDisplay("{'x',nq}")>
        Public ReadOnly _25 As Integer

        <DebuggerDisplay("{new B()}")>
        Public ReadOnly _26_0 As Integer

        <DebuggerDisplay("{new D()}")>
        Public ReadOnly _26_1 As Integer

        <DebuggerDisplay("{new E()}")>
        Public ReadOnly _26_2 As Integer

        <DebuggerDisplay("{ReturnVoid()}")>
        Public ReadOnly _26_3 As Integer

        Private Sub ReturnVoid()
        End Sub

        <DebuggerDisplay("{F1(1)}")>
        Public ReadOnly _26_4 As Integer

        <DebuggerDisplay("{Goo}")>
        Public ReadOnly _26_5 As Integer

        <DebuggerDisplay("{goo}")>
        Public ReadOnly _26_6 As Integer

        Private Function goo() As Integer
            Return 2
        End Function

        Private Function F1(a As Integer) As Integer
            Return 1
        End Function
        Private Function F2(a As Short) As Integer
            Return 2
        End Function

        <DebuggerBrowsable(DebuggerBrowsableState.RootHidden)>
        Public ReadOnly _27_rootHidden As C = New C

        Public ReadOnly _28 As C = New C

        <DebuggerBrowsable(DebuggerBrowsableState.Collapsed)>
        Public ReadOnly _29_collapsed As C = New C

        Public Property _31 As Integer

        <CompilerGenerated>
        Public ReadOnly _32 As Integer

        <CompilerGenerated>
        Private ReadOnly _33 As Integer

        Public ReadOnly Property _34_Exception As Integer
            Get
                Throw New Exception("error1")
            End Get
        End Property

        <DebuggerDisplay("-!-")>
        Public ReadOnly Property _35_Exception As Integer
            Get
                Throw New Exception("error2")
            End Get
        End Property

        Public ReadOnly _36 As Object = New ToStringException

        <DebuggerBrowsable(DebuggerBrowsableState.Never)>
        Public ReadOnly Property _37 As Integer
            Get
                Throw New Exception("error3")
            End Get
        End Property

        Public Property _38_private_get_public_set As Integer
            Private Get
                Return 1
            End Get
            Set(value As Integer)
            End Set
        End Property
        Public Property _39_public_get_private_set As Integer
            Get
                Return 1
            End Get
            Private Set(value As Integer)
            End Set
        End Property
        Private Property _40_private_get_private_set As Integer
            Get
                Return 1
            End Get
            Set(value As Integer)
            End Set
        End Property
        Private WriteOnly Property _41_set_only_property As Integer
            Set(value As Integer)
            End Set
        End Property

        Public Overrides Function ToString() As String
            Return "AStr"
        End Function
    End Class

    <DebuggerTypeProxy(GetType(ComplexProxy))>
    Friend Class TypeWithComplexProxy
        Public Overrides Function ToString() As String
            Return "BStr"
        End Function
    End Class

    <DebuggerTypeProxy(GetType(TypeWithDebuggerDisplayAndProxy.Proxy))>
    <DebuggerDisplay("DD")>
    Friend Class TypeWithDebuggerDisplayAndProxy
        Public Overrides Function ToString() As String
            Return "<ToString>"
        End Function

        <DebuggerDisplay("pxy")>
        Friend Class Proxy
            Public Sub New(x As Object)
            End Sub

            Public ReadOnly A As Integer
            Public ReadOnly B As Integer
        End Class
    End Class

    Friend Class C
        Public ReadOnly A As Integer = 1
        Public ReadOnly B As Integer = 2

        Public Overrides Function ToString() As String
            Return "CStr"
        End Function
    End Class

    <DebuggerDisplay("DebuggerDisplayValue")>
    Friend Class BaseClassWithDebuggerDisplay
    End Class

    Friend Class InheritedDebuggerDisplay
        Inherits BaseClassWithDebuggerDisplay

    End Class

    Friend Class ToStringException
        Public Overrides Function ToString() As String
            Throw New MyException
        End Function
    End Class

    Friend Class MyException
        Inherits Exception

        Public Overrides Function ToString() As String
            Return "my exception"
        End Function
    End Class

    Public Class ThrowingDictionary
        Implements IDictionary

        Private ReadOnly _throwAt As Integer

        Public Sub New(throwAt As Integer)
            _throwAt = throwAt
        End Sub

        Public Sub Add(key As Object, value As Object) Implements IDictionary.Add
            Throw New NotImplementedException
        End Sub

        Public Sub Clear() Implements IDictionary.Clear
            Throw New NotImplementedException
        End Sub

        Public Function Contains(key As Object) As Boolean Implements IDictionary.Contains
            Throw New NotImplementedException
        End Function

        Public Function GetEnumerator() As IDictionaryEnumerator Implements IDictionary.GetEnumerator
            Return New E(_throwAt)
        End Function

        Public ReadOnly Property IsFixedSize As Boolean Implements IDictionary.IsFixedSize
            Get
                Throw New NotImplementedException
            End Get
        End Property

        Public ReadOnly Property IsReadOnly As Boolean Implements IDictionary.IsReadOnly
            Get
                Throw New NotImplementedException
            End Get
        End Property

        Public ReadOnly Property Keys As ICollection Implements IDictionary.Keys
            Get
                Return {1, 2}
            End Get
        End Property

        Public Sub Remove(key As Object) Implements IDictionary.Remove
        End Sub

        Public ReadOnly Property Values As ICollection Implements IDictionary.Values
            Get
                Return {1, 2}
            End Get
        End Property

        Default Public Property Item(key As Object) As Object Implements IDictionary.Item
            Get
                Return 1
            End Get
            Set(value As Object)
            End Set
        End Property

        Public Sub CopyTo(array As Array, index As Integer) Implements System.Collections.ICollection.CopyTo
        End Sub

        Public ReadOnly Property Count As Integer Implements System.Collections.ICollection.Count
            Get
                Return 10
            End Get
        End Property

        Public ReadOnly Property IsSynchronized As Boolean Implements System.Collections.ICollection.IsSynchronized
            Get
                Throw New NotImplementedException
            End Get
        End Property

        Public ReadOnly Property SyncRoot As Object Implements System.Collections.ICollection.SyncRoot
            Get
                Throw New NotImplementedException
            End Get
        End Property

        Private Function IEnumerable_GetEnumerator() As IEnumerator Implements IEnumerable.GetEnumerator
            Return New E(-1)
        End Function

        Private Class E
            Implements IEnumerator, IDictionaryEnumerator

            Private _i As Integer
            Private ReadOnly _throwAt As Integer

            Public Sub New(throwAt As Integer)
                _throwAt = throwAt
            End Sub

            Public ReadOnly Property Current As Object Implements IEnumerator.Current
                Get
                    Return New DictionaryEntry(_i, _i)
                End Get
            End Property

            Public Function MoveNext() As Boolean Implements IEnumerator.MoveNext
                _i += 1
                If _i = _throwAt Then
                    Throw New Exception
                End If

                Return _i < 5
            End Function

            Public Sub Reset() Implements IEnumerator.Reset
            End Sub

            Public ReadOnly Property Entry As DictionaryEntry Implements IDictionaryEnumerator.Entry
                Get
                    Return DirectCast(Current, DictionaryEntry)
                End Get
            End Property

            Public ReadOnly Property Key As Object Implements IDictionaryEnumerator.Key
                Get
                    Return _i
                End Get
            End Property

            Public ReadOnly Property Value As Object Implements IDictionaryEnumerator.Value
                Get
                    Return _i
                End Get
            End Property
        End Class
    End Class

    Public Class ListNode
        Public [next] As ListNode
        Public data As Object
    End Class

    Public Class LongMembers
        Public ReadOnly LongName0123456789_0123456789_0123456789_0123456789_0123456789_0123456789_0123456789 As String = "hello"
        Public ReadOnly LongValue As String = "0123456789_0123456789_0123456789_0123456789_0123456789_0123456789_0123456789"
    End Class

End Namespace
