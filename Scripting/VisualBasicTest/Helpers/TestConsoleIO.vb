Imports System.IO
Imports System.Text
Imports Microsoft.CodeAnalysis.Scripting.Hosting

Friend NotInheritable Class TestConsoleIO
    Inherits ConsoleIO

    Private Const InitialColor As ConsoleColor = ConsoleColor.Gray

    Public Sub New(input As String)
        Me.New(New Reader(input))
    End Sub

    Private Sub New(reader As Reader)
        Me.New(reader, New Writer(reader))
    End Sub

    Private Sub New(reader As Reader, output As TextWriter)
        MyBase.New(output:=output, [error]:=New TeeWriter(output), input:=reader)
    End Sub

    Public Overrides Sub SetForegroundColor(consoleColor As ConsoleColor)
        CType(Out, Writer).CurrentColor = consoleColor
    End Sub

    Public Overrides Sub ResetColor()
        SetForegroundColor(InitialColor)
    End Sub

    Private NotInheritable Class Reader
        Inherits StringReader

        Public ReadOnly ContentRead As StringBuilder = New StringBuilder

        Public Sub New(input As String)
            MyBase.New(input)
        End Sub

        Public Overrides Function ReadLine() As String
            Dim result As String = MyBase.ReadLine()
            ContentRead.AppendLine(result)
            Return result
        End Function
    End Class

    Private NotInheritable Class Writer
        Inherits StringWriter

        Private _lastColor As ConsoleColor = InitialColor
        Public CurrentColor As ConsoleColor = InitialColor
        Public Overrides ReadOnly Property Encoding As Encoding
            Get
                Return Encoding.UTF8
            End Get
        End Property
        Private ReadOnly _reader As Reader

        Public Sub New(reader As Reader)
            _reader = reader
        End Sub

        Private Sub OnBeforeWrite()
            If _reader.ContentRead.Length > 0 Then
                GetStringBuilder().Append(_reader.ContentRead.ToString())
                _reader.ContentRead.Clear()
            End If

            If _lastColor <> CurrentColor Then
                GetStringBuilder().AppendLine($"«{CurrentColor}»")
                _lastColor = CurrentColor
            End If
        End Sub

        Public Overrides Sub Write(value As Char)
            OnBeforeWrite()
            MyBase.Write(value)
        End Sub

        Public Overrides Sub Write(value As String)
            OnBeforeWrite()
            GetStringBuilder().Append(value)
        End Sub

        Public Overrides Sub WriteLine(value As String)
            OnBeforeWrite()
            GetStringBuilder().AppendLine(value)
        End Sub

        Public Overrides Sub WriteLine()
            OnBeforeWrite()
            GetStringBuilder().AppendLine()
        End Sub
    End Class

    Private NotInheritable Class TeeWriter
        Inherits StringWriter

        Public Overrides ReadOnly Property Encoding As Encoding
            Get
                Return Encoding.UTF8
            End Get
        End Property
        Private ReadOnly _other As TextWriter

        Public Sub New(other As TextWriter)
            _other = other
        End Sub

        Public Overrides Sub Write(value As Char)
            _other.Write(value)
            GetStringBuilder().Append(value)
        End Sub

        Public Overrides Sub Write(value As String)
            _other.Write(value)
            GetStringBuilder().Append(value)
        End Sub

        Public Overrides Sub WriteLine(value As String)
            _other.WriteLine(value)
            GetStringBuilder().AppendLine(value)
        End Sub

        Public Overrides Sub WriteLine()
            _other.WriteLine()
            GetStringBuilder().AppendLine()
        End Sub
    End Class
End Class
