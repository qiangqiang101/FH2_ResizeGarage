Imports System.IO
Imports System.Threading
Imports FH2ModPlatform

Public Class ResizeGarage
    Inherits [Mod]

    Private Const GameLimit As Integer = 550
    Private Shared ReadOnly Sizes As Integer() = {550, 1000, 1500, 2000, 3000}

    ' Where the game has 550: the offset of the instruction before it (checked too, so a patch can
    ' only land on the game build this was made for), the bytes up to the number, and after it.
    Private Shared ReadOnly Sites As (Name As String, Offset As Integer, Before As String, After As String)() = {
        ("Garage::IsFull limit", &H2E7809, "48 03 CB 48 81 F9", ""),   ' add rcx,rbx / cmp rcx,550
        ("Garage::AddCar limit", &H2CB6CE, "48 03 C3 48 3D", ""),      ' add rax,rbx / cmp rax,550
        ("garage limit (menus)", &HD2A610, "C3 B8", "C3")              ' ret / mov eax,550 / ret
    }

    Private _iniPath As String = ""
    Private _limit As Integer = 1000

    Public Overrides ReadOnly Property Info As ModInfo
        Get
            Return New ModInfo("ResizeGarage", "1.0.0") With {
                .Description = "Increase your garage limits, holds more than 550 cars.",
                .CheckUpdates = True,
                .GithubRepo = "qiangqiang101/FH2_ResizeGarage"
            }
        End Get
    End Property

    Protected Overrides Sub OnInit()
        _iniPath = Path.Combine(ModPlatform.ModsDir, "ResizeGarage.ini")
        _limit = ReadLimit()

        If ModPlatform.Supports(ApiLevel.TitleEntries) Then   ' choice rows: API 9
            Dim choices = Sizes.ToList()
            If Not choices.Contains(_limit) Then choices.Add(_limit) : choices.Sort()   ' a size typed into the ini
            AddSetting(New ChoiceSetting("MaxCars", "Garage size",
                                         choices.Select(Function(n) If(n = GameLimit, $"{n} (the game's)", $"{n} cars")).ToList(),
                                         choices.IndexOf(_limit)) With {
                .Description = "How many cars your garage holds. If you go above 550 and remove this mod later, you keep every car, but can't get more until you're below 550.",
                .NeedsRestart = True,
                .Changed = Sub(i) WriteLimit(choices(i))
            })
        End If

        ' The game's code may not be there yet in OnInit: wait for it off the game's thread.
        Dim t As New Thread(AddressOf Install) With {.IsBackground = True, .Name = "ResizeGarage start"}
        t.Start()
    End Sub

    Private Sub Install()
        Dim b As IntPtr = ModPlatform.WaitForCode()
        If b = IntPtr.Zero Then
            Log("the game's code never appeared; off")
            Return
        End If
        If _limit = GameLimit Then
            Log($"garage size: {GameLimit} cars, the game's own (nothing changed)")
            Return
        End If

        ' Check all three first: on another game build, change nothing.
        For Each s In Sites
            If Not GameCode.MatchesBuild(b + s.Offset, Expected(s, GameLimit)) Then
                Log($"{s.Name}: the game's code isn't as expected (another game build?), so the garage stays at {GameLimit}")
                Return
            End If
        Next

        Dim made As New List(Of Hook)
        For Each s In Sites
            Dim h = PatchBytes(s.Name, b + s.Offset, Expected(s, _limit), Expected(s, GameLimit))
            If h Is Nothing Then
                Log($"{s.Name}: couldn't change it (another mod there?), so the garage stays at {GameLimit}")
                For Each m In made
                    m.Disable()
                Next
                Return
            End If
            made.Add(h)
        Next
        ' Patches start switched off: this writes all three into the game.
        If Not EnableHooks() Then
            Log($"couldn't switch the changes on (the log above says why), so the garage stays at {GameLimit}")
            For Each m In made
                m.Disable()
            Next
            Return
        End If
        Log($"garage size: {_limit} cars (the game's is {GameLimit})")
    End Sub

    ' A site's bytes with the limit set to limit.
    Private Shared Function Expected(s As (Name As String, Offset As Integer, Before As String, After As String), limit As Integer) As Byte()
        Dim bytes As New List(Of Byte)(GameCode.Hex(s.Before))
        bytes.AddRange(BitConverter.GetBytes(limit))   ' little-endian, as x86 has it
        If s.After <> "" Then bytes.AddRange(GameCode.Hex(s.After))
        Return bytes.ToArray()
    End Function

    ' fh2-mods\ResizeGarage.ini
    Private Function ReadLimit() As Integer
        Try
            If File.Exists(_iniPath) Then
                For Each line In File.ReadAllLines(_iniPath)
                    Dim kv = line.Split("="c, 2)
                    Dim n As Integer
                    If kv.Length = 2 AndAlso kv(0).Trim().Equals("MaxCars", StringComparison.OrdinalIgnoreCase) AndAlso
                       Integer.TryParse(kv(1).Trim(), n) Then
                        Return Math.Clamp(n, GameLimit, 100000)
                    End If
                Next
            Else
                WriteLimit(_limit)   ' a file to find and edit
            End If
        Catch ex As IOException
            Log($"couldn't read {_iniPath}: {ex.Message}")
        Catch ex As UnauthorizedAccessException
            Log($"couldn't read {_iniPath}: {ex.Message}")
        End Try
        Return _limit
    End Function

    Private Sub WriteLimit(n As Integer)
        Try
            File.WriteAllText(_iniPath,
                "; Garage Size: how many cars your garage holds (the game's own is 550)." & vbCrLf &
                "; Read when the game starts. Also on Pause > Settings > MODS." & vbCrLf &
                "[ResizeGarage]" & vbCrLf &
                $"MaxCars={n}" & vbCrLf)
        Catch ex As IOException
            Log($"couldn't write {_iniPath}: {ex.Message}")
        Catch ex As UnauthorizedAccessException
            Log($"couldn't write {_iniPath}: {ex.Message}")
        End Try
    End Sub

End Class
