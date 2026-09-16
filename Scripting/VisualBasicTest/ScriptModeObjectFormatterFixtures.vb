' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

' U8 的夹具，被 ScriptModeObjectFormatterTests.vb 的用例断言。
' 这里放的三类夹具都**不能**放进 Helpers\ObjectFormatterFixtures.vb（该文件是本单元的只读输入），
' 也不能放进测试类内部（见下），故按 C# 基线的分层方式落在测试程序集自己的文件里。

Option Strict Off

Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Runtime.CompilerServices
Imports System.Threading
Imports System.Threading.Tasks

Namespace Global.ObjectFormatterFixtures

    ''' <summary>
    ''' C# 夹具 {{Roslyn}}\src\Scripting\CoreTestUtilities\ObjectFormatterFixtures\MockDesktopTask.cs 的 VB 形状。
    ''' 对应 C# 基线 `DebuggerProxy_FrameworkTypes_Task`（ObjectFormatterTests.cs:788）。
    ''' 定义在**顶层命名空间**（C# 那份也在 `ObjectFormatterFixtures` 命名空间里，只是分了文件）：
    ''' 嵌套在测试类里会让类型名带上 `ScriptModeObjectFormatterTests.` 前缀，与 C# 基线不可比。
    ''' </summary>
    <DebuggerTypeProxy(GetType(MockTaskProxy))>
    <DebuggerDisplay("Id = {Id}, Status = {Status}, Method = {DebuggerDisplayMethodDescription}")>
    Friend NotInheritable Class MockDesktopTask
        Private ReadOnly m_action As Action

        Public Sub New(action As Action)
            m_action = action
        End Sub

        Public ReadOnly Property Id As Integer
            Get
                Return 1234
            End Get
        End Property

        Public ReadOnly Property AsyncState As Object
            Get
                Return Nothing
            End Get
        End Property

        Public ReadOnly Property CreationOptions As TaskCreationOptions
            Get
                Return TaskCreationOptions.None
            End Get
        End Property

        Public ReadOnly Property Exception As Exception
            Get
                Return Nothing
            End Get
        End Property

        Public ReadOnly Property Status As TaskStatus
            Get
                Return TaskStatus.Created
            End Get
        End Property

        ''' <summary>DD 的 `Method` 段取的是**私有**属性的值——这一条正是该格要钉的行为之一。</summary>
        Private ReadOnly Property DebuggerDisplayMethodDescription As String
            Get
                Return m_action.Method.ToString()
            End Get
        End Property
    End Class

    Friend NotInheritable Class MockTaskProxy
        Private ReadOnly m_task As MockDesktopTask

        Public Sub New(task As MockDesktopTask)
            m_task = task
        End Sub

        Public ReadOnly Property AsyncState As Object
            Get
                Return m_task.AsyncState
            End Get
        End Property

        Public ReadOnly Property CreationOptions As TaskCreationOptions
            Get
                Return m_task.CreationOptions
            End Get
        End Property

        Public ReadOnly Property Exception As Exception
            Get
                Return m_task.Exception
            End Get
        End Property

        Public ReadOnly Property Id As Integer
            Get
                Return m_task.Id
            End Get
        End Property

        Public ReadOnly Property CancellationPending As Boolean
            Get
                Return False
            End Get
        End Property

        Public ReadOnly Property Status As TaskStatus
            Get
                Return m_task.Status
            End Get
        End Property
    End Class

    ''' <summary>
    ''' C# 夹具 {{Roslyn}}\src\Scripting\CoreTestUtilities\ObjectFormatterFixtures\MockDesktopSpinLock.cs 的 VB 形状。
    ''' 对应 C# 基线 `DebuggerProxy_FrameworkTypes_SpinLock1/2`（ObjectFormatterTests.cs:810/826）。
    ''' </summary>
    <DebuggerTypeProxy(GetType(MockDesktopSpinLock.SpinLockDebugView))>
    <DebuggerDisplay("IsHeld = {IsHeld}")>
    Friend Structure MockDesktopSpinLock
        Private ReadOnly m_owner As Integer

        Public Sub New(enableThreadOwnerTracking As Boolean)
            m_owner = If(enableThreadOwnerTracking, 0, Integer.MinValue)
        End Sub

        Public ReadOnly Property IsHeld As Boolean
            Get
                Return False
            End Get
        End Property

        ''' <summary>关掉 owner tracking 时抛异常——这是 C# 基线 `SpinLock1` 那一格的 `!&lt;InvalidOperationException&gt;` 的来源。</summary>
        Public ReadOnly Property IsHeldByCurrentThread As Boolean
            Get
                If Not IsThreadOwnerTrackingEnabled Then Throw New InvalidOperationException("Error")
                Return True
            End Get
        End Property

        Public ReadOnly Property IsThreadOwnerTrackingEnabled As Boolean
            Get
                Return (m_owner And Integer.MinValue) = 0
            End Get
        End Property

        Friend NotInheritable Class SpinLockDebugView
            Private ReadOnly m_spinLock As MockDesktopSpinLock

            Public Sub New(spinLock As MockDesktopSpinLock)
                m_spinLock = spinLock
            End Sub

            Public ReadOnly Property IsHeldByCurrentThread As Boolean?
                Get
                    Return m_spinLock.IsHeldByCurrentThread
                End Get
            End Property

            Public ReadOnly Property OwnerThreadID As Integer?
                Get
                    Return If(m_spinLock.IsThreadOwnerTrackingEnabled, m_spinLock.m_owner, CType(Nothing, Integer?))
                End Get
            End Property

            Public ReadOnly Property IsHeld As Boolean
                Get
                    Return m_spinLock.IsHeld
                End Get
            End Property
        End Class
    End Structure

    ''' <summary>
    ''' C# 基线把 `Range_Core`/`RangeIterator` 定义在测试类内部（ObjectFormatterTests.cs:458-496），
    ''' 本文件把它们放在**顶层**类里，理由同上（`FormatObject` 的 `showNamespaces:=False`，嵌套类型会印成
    ''' `外层类名.自身名`，把容器类名带进期望值，与 C# 基线不可比）。
    ''' </summary>
    Friend NotInheritable Class RangeFixtures

        ''' <summary>模拟 .NET Core 的 `Enumerable.Range`：只带 DD、**不实现** IDictionary/ICollection。</summary>
        Public Shared Function Range_Core(start As Integer, count As Integer) As IEnumerable(Of Integer)
            Return New CoreRangeIterator(start, count)
        End Function

    End Class

    ''' <summary>
    ''' 模拟 .NET Framework 的 `Enumerable.Range`：真正的迭代器，编译成状态机类型。
    ''' **这个类里只放一个迭代器**：状态机名 `VB$StateMachine_&lt;n&gt;_…` 的 `n` 是该类内合成类型的序号，
    ''' 成员一增删就会变（踩坑点）；独占一个类可把该序号钉在 1。
    ''' </summary>
    Friend NotInheritable Class IteratorHost

        Public Shared Function Range_Framework(start As Integer, count As Integer) As IEnumerable(Of Integer)
            Return IteratorRange(start, count)
        End Function

        Private Shared Iterator Function IteratorRange(start As Integer, count As Integer) As IEnumerable(Of Integer)
            For i = 0 To count - 1
                Yield start + i
            Next
        End Function

    End Class

    <DebuggerDisplay("Count = {CountForDebugger}")>
    Friend NotInheritable Class CoreRangeIterator
        Implements IEnumerable(Of Integer)

        Private ReadOnly _start As Integer
        Private ReadOnly _end As Integer

        Private ReadOnly Property CountForDebugger As Integer
            Get
                Return _end - _start
            End Get
        End Property

        Public Sub New(start As Integer, count As Integer)
            _start = start
            _end = start + count
        End Sub

        Public Function GetEnumerator() As IEnumerator(Of Integer) Implements IEnumerable(Of Integer).GetEnumerator
            Return Nothing
        End Function

        Private Function IEnumerable_GetEnumerator() As IEnumerator Implements IEnumerable.GetEnumerator
            Return Nothing
        End Function
    End Class

End Namespace

' ==================================================================================================================
' 晚期绑定夹具。**单独放在本文件**是为了拿到文件级的 `Option Strict Off`（见文件头）：
' VB 没有 `dynamic` 关键字，C# 基线 `StackTrace_Dynamic` 的 `((dynamic)o).x()` 在 VB 的对应物就是晚期绑定调用。
' 本区域也自带 `#ExternalSource`，故它的帧映射到 `z:\Fixture.vb`；基址取 20000 以区别于测试文件里那组（10000）。
' ==================================================================================================================
#ExternalSource("z:\Fixture.vb", 20000)

Namespace Global.ObjectFormatterFixtures

    Friend NotInheritable Class DynamicStackFixture
        <MethodImpl(MethodImplOptions.NoInlining)>
        Public Shared Sub MethodDynamic()
            Dim o As Object = New Object()
            o.x()
        End Sub
    End Class

End Namespace

#End ExternalSource
