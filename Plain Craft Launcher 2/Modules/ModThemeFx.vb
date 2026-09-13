Imports System.Windows
Imports System.Windows.Controls
Imports System.Windows.Input
Imports System.Windows.Media
Imports System.Windows.Media.Imaging

''' <summary>
''' 幻星修改版：主题动态背景与粒子特效层。
''' 挂载于主窗口背景图片之上、界面内容之下，提供：
''' 1. 主题动态背景（星空深空 / 原神星穹 / 四季物候 / 娱乐彩虹；用户设置了背景图片时自动让位）；
''' 2. 氛围粒子（星星、流星、樱花瓣、六角雪花、枫叶、流萤、气球、元素光尘等，随主题变化）；
''' 3. 点击星星迸发特效；
''' 4. 主题立绘展示：在 PCL\Pictures\主题立绘\原神（或星空/四季/娱乐）文件夹放入图片即可自动展示。
''' 密度与大小可在个性化设置调节并可整体关闭，低性能设备自动降级。
''' 特效层的任何异常都不允许影响启动器本体运行。
''' </summary>
Friend Module ModThemeFx

#Region "内部类型"

    Private Class FxParticle
        Public Visual As FrameworkElement = Nothing
        ''' <summary>当前 Visual 对应的形状类别（-1 未创建），与 Kind 的形状映射不同时需重建。</summary>
        Public VisualKind As Integer = -1
        Public Kind As Integer '0 光点 1 星星 2 彩带 3 樱瓣 4 雪花 8 枫叶 6 流萤 7 流星 9 气球 10 光晕
        Public X As Double = 0, Y As Double = 0
        Public VX As Double = 0, VY As Double = 0
        Public Age As Double = 0
        Public Life As Double = 1
        Public Size As Double = 4
        Public Spin As Double = 0
        Public Phase As Double = 0
        Public BaseOpacity As Double = 1
        Public Rotate As RotateTransform = Nothing
    End Class

    ''' <summary>粒子的运动类型到形状类别的映射（形状复用时按此判断是否需要重建）。</summary>
    Private Function ShapeKindOf(Kind As Integer) As Integer
        Select Case Kind
            Case 1, 6 : Return 1 '星星
            Case 2 : Return 2 '彩带
            Case 3 : Return 3 '樱瓣
            Case 4 : Return 4 '雪花
            Case 5, 8 : Return 8 '枫叶
            Case 7 : Return 7 '流星（渐变拖尾矩形）
            Case 9 : Return 9 '气球
            Case Else : Return 0 '圆形
        End Select
    End Function

#End Region

#Region "状态"

    Private FxCanvas As Canvas = Nothing
    Private ReadOnly Actives As New List(Of FxParticle)
    Private ReadOnly Spares As New List(Of FxParticle)
    Private ReadOnly Rand As New Random
    Private RenderHooked As Boolean = False
    Private MouseHooked As Boolean = False
    Private LastTime As TimeSpan = TimeSpan.Zero
    Private SlowTick As Integer = 0
    Private ThrottleHinted As Boolean = False
    Private LastStyle As Integer = -1, LastSeason As Integer = -1
    Private MeteorCountdown As Double = 2.5

    ''' <summary>当前特效风格：0 主题色光点，2 星空，3 四季，4 原神，5 娱乐。</summary>
    Public FxStyle As Integer = 0
    ''' <summary>四季子模式：1 春 2 夏 3 秋 4 冬。</summary>
    Public SeasonMode As Integer = 1

    Private TargetCount As Integer = 0
    Private BurstCount As Integer = 0
    Private SizeScale As Double = 1.0

    Private ThemeArt As Image = Nothing '主题立绘

    '形状几何（懒加载并冻结）
    Private GeoStar As Geometry = Nothing, GeoPetal As Geometry = Nothing, GeoSnow As Geometry = Nothing
    Private GeoMaple As Geometry = Nothing, GeoBalloon As Geometry = Nothing

    '原神七元素颜色（风岩雷草水火冰，近似）
    Private ReadOnly GenshinElements As Color() = {
        Color.FromRgb(95, 180, 162), Color.FromRgb(224, 167, 41), Color.FromRgb(169, 127, 168),
        Color.FromRgb(140, 178, 62), Color.FromRgb(85, 200, 240), Color.FromRgb(232, 112, 58), Color.FromRgb(168, 220, 236)}

    Private Function Geom(ByRef Cache As Geometry, Data As String) As Geometry
        If Cache Is Nothing Then
            Cache = Geometry.Parse(Data)
            Cache.Freeze()
        End If
        Return Cache
    End Function

#End Region

#Region "对外接口"

    ''' <summary>按当前主题与设置刷新特效层（主题切换、特效设置变化时调用）。</summary>
    Public Sub FxRefresh()
        RunInUi(Sub() FxRefreshCore())
    End Sub

    ''' <summary>彩虹类主题（娱乐/滑稽彩）的背景循环推进，由 250ms 计时器调用。</summary>
    Public Sub RainbowBgTick()
        RunInUi(
        Sub()
            Try
                If FxStyle <> 5 OrElse FxCanvas Is Nothing OrElse FxCanvas.Parent Is Nothing Then Return
                If HasWallpaper() Then Return
                FxCanvas.Background = MakeRainbowBackground(ColorHue)
            Catch ex As Exception
                Logger.Error(ex, "幻星特效彩虹背景更新失败")
            End Try
        End Sub)
    End Sub

#End Region

#Region "动态背景"

    Private Function HasWallpaper() As Boolean
        Return FrmMain IsNot Nothing AndAlso FrmMain.ImgBack IsNot Nothing AndAlso FrmMain.ImgBack.Background IsNot Nothing
    End Function

    Private Sub ApplyBackground()
        If FxCanvas Is Nothing Then Return
        '用户设置了背景图片时，动态背景自动让位（仍保留粒子与立绘）
        If HasWallpaper() Then
            FxCanvas.Background = Nothing
            Return
        End If
        Select Case FxStyle
            Case 2 '星空 · 深空夜幕
                FxCanvas.Background = MakeVerticalGradient(Color.FromRgb(7, 11, 30), Color.FromRgb(16, 26, 60), Color.FromRgb(27, 44, 94))
            Case 4 '原神 · 星穹夜空（深蓝夜空底 + 金色星愿，呼应神之眼视觉母题）
                FxCanvas.Background = MakeVerticalGradient(Color.FromRgb(14, 18, 38), Color.FromRgb(27, 35, 71), Color.FromRgb(61, 48, 84))
            Case 3 '四季 · 季节浅色天幕
                Select Case SeasonMode
                    Case 1 : FxCanvas.Background = MakeVerticalGradient(Color.FromRgb(255, 238, 243), Color.FromRgb(233, 249, 236)) '春 · 樱粉嫩绿
                    Case 2 : FxCanvas.Background = MakeVerticalGradient(Color.FromRgb(226, 246, 255), Color.FromRgb(219, 244, 233)) '夏 · 清凉水色
                    Case 3 : FxCanvas.Background = MakeVerticalGradient(Color.FromRgb(255, 244, 226), Color.FromRgb(255, 226, 199)) '秋 · 暖橙
                    Case Else : FxCanvas.Background = MakeVerticalGradient(Color.FromRgb(240, 247, 255), Color.FromRgb(222, 236, 249)) '冬 · 霜蓝
                End Select
            Case Else
                FxCanvas.Background = Nothing
        End Select
    End Sub

    Private Function MakeVerticalGradient(ParamArray Colors As Color()) As LinearGradientBrush
        Dim Brush As New LinearGradientBrush With {.StartPoint = New Point(0, 0), .EndPoint = New Point(0, 1)}
        Dim StepValue = If(Colors.Length > 1, 1.0 / (Colors.Length - 1), 1.0)
        For i = 0 To Colors.Length - 1
            Brush.GradientStops.Add(New GradientStop With {.Color = Colors(i), .Offset = StepValue * i})
        Next
        Brush.Freeze()
        Return Brush
    End Function

    Private Function MakeRainbowBackground(BaseHue As Integer) As LinearGradientBrush
        Dim Brush As New LinearGradientBrush With {.StartPoint = New Point(0, 0), .EndPoint = New Point(0, 1)}
        For i = 0 To 5
            Dim Col = CType(New MyColor().FromHSL2((BaseHue + i * 52) Mod 361, 62, If(i Mod 2 = 0, 84, 78)), Color)
            Brush.GradientStops.Add(New GradientStop With {.Color = Col, .Offset = i / 5.0})
        Next
        Brush.Freeze()
        Return Brush
    End Function

#End Region

#Region "主题立绘"

    ''' <summary>加载主题立绘：PCL\Pictures\主题立绘\ 主题名 文件夹下的随机图片。</summary>
    Private Sub SetupThemeArt()
        If ThemeArt IsNot Nothing Then
            FxCanvas.Children.Remove(ThemeArt)
            ThemeArt = Nothing
        End If
        Dim Name As String = ""
        Select Case FxStyle
            Case 4 : Name = "原神"
            Case 2 : Name = "星空"
            Case 3 : Name = "四季"
            Case 5 : Name = "娱乐"
            Case Else : Return
        End Select
        Try
            Dim DirPath = Paths.Base & "PCL\Pictures\主题立绘\" & Name & "\"
            If Not Directory.Exists(DirPath) Then Return
            Dim Valid As New List(Of String)
            For Each File In Directory.GetFiles(DirPath)
                Dim Ext = IO.Path.GetExtension(File).ToLower()
                If Ext = ".png" OrElse Ext = ".jpg" OrElse Ext = ".jpeg" OrElse Ext = ".bmp" OrElse Ext = ".webp" Then Valid.Add(File)
            Next
            If Valid.Count = 0 Then Return
            Dim Bmp As New BitmapImage
            Bmp.BeginInit()
            Bmp.CacheOption = BitmapCacheOption.OnLoad
            Bmp.UriSource = New Uri(Valid(Rand.Next(Valid.Count)))
            Bmp.EndInit()
            Bmp.Freeze()
            ThemeArt = New Image With {.Source = Bmp, .IsHitTestVisible = False, .Opacity = 0.97}
            FxCanvas.Children.Insert(0, ThemeArt) '立绘在最底层：背景之上、粒子之下
        Catch ex As Exception
            Logger.Error(ex, "加载主题立绘失败")
        End Try
    End Sub

    Private Sub UpdateThemeArt()
        If ThemeArt Is Nothing OrElse ThemeArt.Source Is Nothing Then Return
        Dim H = FxCanvas.ActualHeight, W = FxCanvas.ActualWidth
        If H < 120 OrElse W < 240 Then Return
        Dim Scale = Math.Min(H * 0.95 / ThemeArt.Source.Height, W * 0.52 / ThemeArt.Source.Width)
        If Scale <= 0 OrElse Double.IsInfinity(Scale) OrElse Double.IsNaN(Scale) Then Return
        Dim TargetW = ThemeArt.Source.Width * Scale
        Dim TargetH = ThemeArt.Source.Height * Scale
        ThemeArt.Width = TargetW
        ThemeArt.Height = TargetH
        Dim Bob = Math.Sin(LastTime.TotalSeconds * 1.1) * 5 '轻微呼吸浮动
        Canvas.SetLeft(ThemeArt, W - TargetW - 14)
        Canvas.SetTop(ThemeArt, H - TargetH + 10 + Bob)
    End Sub

#End Region

#Region "挂载与卸载"

    Private Sub FxRefreshCore()
        Try
            Dim Level As Integer = Math.Max(0, Math.Min(3, Settings.Get(Of Integer)("UiFxLevel")))
            Dim SizeSet As Integer = Math.Max(1, Math.Min(5, Settings.Get(Of Integer)("UiFxSize")))
            SizeScale = 0.55 + 0.3 * SizeSet
            Select Case Level
                Case 0 : TargetCount = 0 : BurstCount = 0
                Case 1 : TargetCount = 26 : BurstCount = 9
                Case 2 : TargetCount = 60 : BurstCount = 14
                Case Else : TargetCount = 115 : BurstCount = 22
            End Select
            If TargetCount <= 0 OrElse FrmMain Is Nothing OrElse Not FrmMain.IsLoaded Then
                Detach()
                Return
            End If
            If FxStyle <> LastStyle OrElse (FxStyle = 3 AndAlso SeasonMode <> LastSeason) Then
                ClearParticles()
                LastStyle = FxStyle
                LastSeason = SeasonMode
                MeteorCountdown = 1.5 + Rand.NextDouble() * 2
            End If
            Attach()
            ApplyBackground()
            SetupThemeArt()
        Catch ex As Exception
            Logger.Error(ex, "幻星特效刷新失败")
        End Try
    End Sub

    Private Sub Attach()
        If FxCanvas Is Nothing Then
            FxCanvas = New Canvas With {.IsHitTestVisible = False, .ClipToBounds = True}
        End If
        If FxCanvas.Parent Is Nothing Then
            Dim Index As Integer = FrmMain.PanForm.Children.IndexOf(FrmMain.ImgBack)
            If Index < 0 Then Index = 0
            FrmMain.PanForm.Children.Insert(Index + 1, FxCanvas)
            Grid.SetRow(FxCanvas, 1)
        End If
        If Not RenderHooked Then
            AddHandler CompositionTarget.Rendering, AddressOf OnRendering
            RenderHooked = True
        End If
        If Not MouseHooked Then
            AddHandler FrmMain.PreviewMouseDown, AddressOf OnPreviewMouseDown
            MouseHooked = True
        End If
    End Sub

    Private Sub Detach()
        If RenderHooked Then
            RemoveHandler CompositionTarget.Rendering, AddressOf OnRendering
            RenderHooked = False
        End If
        If MouseHooked AndAlso FrmMain IsNot Nothing Then
            RemoveHandler FrmMain.PreviewMouseDown, AddressOf OnPreviewMouseDown
            MouseHooked = False
        End If
        If FxCanvas IsNot Nothing AndAlso FxCanvas.Parent IsNot Nothing Then
            CType(FxCanvas.Parent, Panel).Children.Remove(FxCanvas)
            FxCanvas.Background = Nothing
        End If
        ThemeArt = Nothing
        ClearParticles()
        LastStyle = -1
    End Sub

    Private Sub ClearParticles()
        For Each P In Actives
            If P.Visual IsNot Nothing AndAlso FxCanvas IsNot Nothing Then FxCanvas.Children.Remove(P.Visual)
            P.VisualKind = -1
            Spares.Add(P)
        Next
        Actives.Clear()
    End Sub

#End Region

#Region "帧驱动"

    Private Sub OnRendering(sender As Object, e As EventArgs)
        Try
            If FxCanvas Is Nothing OrElse FxCanvas.Parent Is Nothing Then Return
            Dim Args = CType(e, RenderingEventArgs)
            Dim Dt As Double = (Args.RenderingTime - LastTime).TotalSeconds
            LastTime = Args.RenderingTime
            If Dt <= 0 OrElse Dt >= 0.5 Then Return '首帧或窗口挂起恢复
            Dim W As Double = If(FxCanvas.ActualWidth < 50, 900, FxCanvas.ActualWidth)
            Dim H As Double = If(FxCanvas.ActualHeight < 50, 560, FxCanvas.ActualHeight)
            '低性能自动降级
            If Dt > 0.075 Then SlowTick += 1 Else If SlowTick > 0 Then SlowTick -= 1
            If SlowTick > 120 AndAlso TargetCount > 26 Then
                TargetCount = 26
                If Not ThrottleHinted Then
                    ThrottleHinted = True
                    Hint("检测到运行帧率较低，已自动减少主题特效粒子；也可以在设置中关闭特效")
                End If
            End If
            '流星调度（星空 / 原神）
            If FxStyle = 2 OrElse FxStyle = 4 Then
                MeteorCountdown -= Dt
                If MeteorCountdown <= 0 Then
                    SpawnMeteor(W, H)
                    MeteorCountdown = 3.5 + Rand.NextDouble() * 5
                End If
            End If
            '补充氛围粒子（有界循环，防止异常情况下死循环）
            Dim Safety As Integer = 8
            Do While Actives.Count < TargetCount AndAlso Safety > 0
                SpawnAmbient(W, H)
                Safety -= 1
            Loop
            '逐帧更新
            For i = Actives.Count - 1 To 0 Step -1
                Dim P = Actives(i)
                P.Age += Dt
                If P.Age >= P.Life Then
                    Recycle(P)
                    Actives.RemoveAt(i)
                    Continue For
                End If
                UpdateParticle(P, Dt, W, H)
            Next
            UpdateThemeArt()
        Catch ex As Exception
            Logger.Error(ex, "幻星特效帧驱动异常")
        End Try
    End Sub

    Private Sub UpdateParticle(P As FxParticle, Dt As Double, W As Double, H As Double)
        Select Case P.Kind
            Case 6 '流萤：随机游走
                P.VX += (Rand.NextDouble() - 0.5) * 120 * Dt
                P.VY += (Rand.NextDouble() - 0.5) * 120 * Dt
                P.VX = Math.Max(-28, Math.Min(28, P.VX))
                P.VY = Math.Max(-20, Math.Min(20, P.VY))
            Case 1, 2 '星星迸发 / 彩带：受重力
                P.VY += 240 * Dt
            Case 9 '气球：上浮 + 摇摆
                P.X += Math.Sin(P.Age * 1.3 + P.Phase) * 14 * Dt
        End Select
        P.X += P.VX * Dt
        P.Y += P.VY * Dt
        '氛围粒子的边界环绕（流星等短寿命粒子除外）
        If P.Life > 3 AndAlso P.Age < P.Life - 0.05 Then
            If P.Y > H + 20 Then
                P.Y = -15
            ElseIf P.Y < -25 Then
                P.Y = H + 10
            End If
            If P.X > W + 20 Then
                P.X = -15
            ElseIf P.X < -20 Then
                P.X = W + 10
            End If
        End If
        '淡入淡出（光点类额外闪烁）
        Dim T = Math.Min(1, P.Age / P.Life)
        Dim Opacity = P.BaseOpacity * Math.Sin(Math.PI * T)
        If P.Kind = 0 OrElse P.Kind = 1 OrElse P.Kind = 6 Then
            Opacity *= 0.6 + 0.4 * Math.Sin(P.Age * 2.6 + P.Phase)
        End If
        P.Visual.Opacity = Math.Max(0, Math.Min(1, Opacity))
        Canvas.SetLeft(P.Visual, P.X)
        Canvas.SetTop(P.Visual, P.Y)
        If P.Rotate IsNot Nothing AndAlso P.Spin <> 0 Then P.Rotate.Angle += P.Spin * Dt
    End Sub

#End Region

#Region "生成"

    Private Sub SpawnAmbient(W As Double, H As Double)
        Select Case FxStyle
            Case 2 : SpawnStarAmbient(W, H)
            Case 3 : SpawnSeasonAmbient(W, H)
            Case 4 : SpawnGenshinAmbient(W, H)
            Case 5 : SpawnFunAmbient(W, H)
            Case Else : SpawnMoteAmbient(W, H)
        End Select
    End Sub

    Private Function CountKind(Kind As Integer) As Integer
        Dim Count As Integer = 0
        For Each P In Actives
            If P.Kind = Kind Then Count += 1
        Next
        Return Count
    End Function

    Private Sub SpawnMoteAmbient(W As Double, H As Double)
        '主题色光点：缓慢上浮，微幅摆动
        Dim Size = (2.5 + Rand.NextDouble() * 3) * SizeScale
        Dim Col = CType(New MyColor().FromHSL2(ColorHue + Rand.Next(-10, 11), ColorSat, 62 + Rand.Next(-8, 9)), Color)
        NewParticle(0, Col, Rand.NextDouble() * W, H + 10, (Rand.NextDouble() - 0.5) * 8, -(8 + Rand.NextDouble() * 16),
                    14 + Rand.NextDouble() * 14, Size, (Rand.NextDouble() - 0.5) * 20, 0.45)
    End Sub

    Private Sub SpawnStarAmbient(W As Double, H As Double)
        '星空：闪烁群星 + 微尘 + 深空光晕
        Dim Roll = Rand.NextDouble()
        If Roll < 0.18 AndAlso CountKind(10) < 4 Then
            '深空光晕
            Dim List = {Color.FromArgb(52, 92, 107, 192), Color.FromArgb(44, 130, 92, 192), Color.FromArgb(40, 63, 81, 181)}
            NewParticle(10, List(Rand.Next(List.Length)), Rand.NextDouble() * W, Rand.NextDouble() * H,
                        (Rand.NextDouble() - 0.5) * 6, (Rand.NextDouble() - 0.5) * 6,
                        22 + Rand.NextDouble() * 16, (160 + Rand.NextDouble() * 170) * SizeScale, 0, 1)
        ElseIf Roll < 0.72 Then
            Dim Size = (2.5 + Rand.NextDouble() * 5) * SizeScale
            Dim List = {Color.FromRgb(255, 255, 255), Color.FromRgb(207, 228, 255), Color.FromRgb(179, 157, 255)}
            NewParticle(1, List(Rand.Next(List.Length)), Rand.NextDouble() * W, Rand.NextDouble() * H,
                        (Rand.NextDouble() - 0.5) * 6, 2 + Rand.NextDouble() * 5,
                        8 + Rand.NextDouble() * 10, Size, (Rand.NextDouble() - 0.5) * 12, 0.95)
        Else
            Dim Size = (1.5 + Rand.NextDouble() * 2.5) * SizeScale
            NewParticle(0, Color.FromRgb(220, 232, 255), Rand.NextDouble() * W, Rand.NextDouble() * H,
                        (Rand.NextDouble() - 0.5) * 10, 3 + Rand.NextDouble() * 8,
                        6 + Rand.NextDouble() * 8, Size, 0, 0.6)
        End If
    End Sub

    Private Sub SpawnSeasonAmbient(W As Double, H As Double)
        Select Case SeasonMode
            Case 1 '春 · 樱瓣纷落
                If Rand.NextDouble() < 0.7 Then
                    Dim List = {Color.FromRgb(255, 183, 197), Color.FromRgb(255, 214, 224), Color.FromRgb(255, 240, 245)}
                    Dim Size = (5 + Rand.NextDouble() * 5) * SizeScale
                    NewParticle(3, List(Rand.Next(List.Length)), Rand.NextDouble() * W, -14,
                                (Rand.NextDouble() - 0.5) * 16, 26 + Rand.NextDouble() * 24,
                                12 + Rand.NextDouble() * 10, Size, (Rand.NextDouble() - 0.5) * 100, 0.85)
                Else
                    Dim Size = (2 + Rand.NextDouble() * 2.5) * SizeScale
                    NewParticle(0, Color.FromRgb(255, 214, 224), Rand.NextDouble() * W, H + 8,
                                (Rand.NextDouble() - 0.5) * 8, -(6 + Rand.NextDouble() * 10),
                                10 + Rand.NextDouble() * 10, Size, 0, 0.5)
                End If
            Case 2 '夏 · 流萤与水色微光
                If Rand.NextDouble() < 0.62 Then
                    Dim Size = (2.5 + Rand.NextDouble() * 3) * SizeScale
                    NewParticle(6, Color.FromRgb(226, 255, 140), Rand.NextDouble() * W, H * (0.2 + Rand.NextDouble() * 0.7),
                                0, 0, 10 + Rand.NextDouble() * 10, Size, 0, 0.9)
                Else
                    Dim Size = (2 + Rand.NextDouble() * 2.5) * SizeScale
                    NewParticle(0, Color.FromRgb(160, 235, 255), Rand.NextDouble() * W, H + 8,
                                (Rand.NextDouble() - 0.5) * 10, -(10 + Rand.NextDouble() * 14),
                                10 + Rand.NextDouble() * 10, Size, 0, 0.55)
                End If
            Case 3 '秋 · 枫叶飘零
                If Rand.NextDouble() < 0.74 Then
                    Dim List = {Color.FromRgb(224, 122, 42), Color.FromRgb(199, 88, 34), Color.FromRgb(233, 166, 52), Color.FromRgb(178, 60, 30)}
                    Dim Size = (6 + Rand.NextDouble() * 6) * SizeScale
                    NewParticle(8, List(Rand.Next(List.Length)), Rand.NextDouble() * W, -16,
                                (Rand.NextDouble() - 0.5) * 20, 44 + Rand.NextDouble() * 34,
                                9 + Rand.NextDouble() * 8, Size, (Rand.NextDouble() - 0.5) * 180, 0.92)
                Else
                    Dim Size = (2 + Rand.NextDouble() * 3) * SizeScale
                    NewParticle(0, Color.FromRgb(250, 200, 120), Rand.NextDouble() * W, H + 8,
                                (Rand.NextDouble() - 0.5) * 10, -(8 + Rand.NextDouble() * 12),
                                10 + Rand.NextDouble() * 10, Size, 0, 0.55)
                End If
            Case Else '冬 · 六角初雪
                If Rand.NextDouble() < 0.8 Then
                    Dim Size = (3.5 + Rand.NextDouble() * 5) * SizeScale
                    NewParticle(4, Color.FromRgb(255, 255, 255), Rand.NextDouble() * W, -14,
                                (Rand.NextDouble() - 0.5) * 18, 20 + Rand.NextDouble() * 26,
                                15 + Rand.NextDouble() * 13, Size, (Rand.NextDouble() - 0.5) * 60, 0.9)
                Else
                    Dim Size = (1.5 + Rand.NextDouble() * 2.5) * SizeScale
                    NewParticle(0, Color.FromRgb(224, 240, 255), Rand.NextDouble() * W, H + 8,
                                (Rand.NextDouble() - 0.5) * 10, -(6 + Rand.NextDouble() * 10),
                                12 + Rand.NextDouble() * 10, Size, 0, 0.6)
                End If
        End Select
    End Sub

    Private Sub SpawnGenshinAmbient(W As Double, H As Double)
        '原神：金色星愿光尘 + 七元素光点 + 深空光晕
        Dim Roll = Rand.NextDouble()
        If Roll < 0.14 AndAlso CountKind(10) < 4 Then
            Dim List = {Color.FromArgb(56, 255, 205, 112), Color.FromArgb(46, 120, 96, 192), Color.FromArgb(40, 62, 84, 168)}
            NewParticle(10, List(Rand.Next(List.Length)), Rand.NextDouble() * W, Rand.NextDouble() * H,
                        (Rand.NextDouble() - 0.5) * 6, (Rand.NextDouble() - 0.5) * 6,
                        22 + Rand.NextDouble() * 16, (150 + Rand.NextDouble() * 170) * SizeScale, 0, 1)
        ElseIf Roll < 0.52 Then
            '金色星愿光尘
            Dim List = {Color.FromRgb(255, 217, 143), Color.FromRgb(255, 236, 187), Color.FromRgb(255, 199, 90)}
            Dim Size = (2.5 + Rand.NextDouble() * 3.5) * SizeScale
            NewParticle(0, List(Rand.Next(List.Length)), Rand.NextDouble() * W, H + 10,
                        (Rand.NextDouble() - 0.5) * 10, -(12 + Rand.NextDouble() * 22),
                        12 + Rand.NextDouble() * 12, Size, (Rand.NextDouble() - 0.5) * 24, 0.75)
        ElseIf Roll < 0.68 Then
            '七元素光点
            Dim Size = (2.5 + Rand.NextDouble() * 3) * SizeScale
            NewParticle(1, GenshinElements(Rand.Next(GenshinElements.Length)), Rand.NextDouble() * W, H + 10,
                        (Rand.NextDouble() - 0.5) * 8, -(9 + Rand.NextDouble() * 16),
                        10 + Rand.NextDouble() * 10, Size, (Rand.NextDouble() - 0.5) * 30, 0.8)
        Else
            '金色星芒
            Dim Size = (3 + Rand.NextDouble() * 4) * SizeScale
            NewParticle(1, Color.FromRgb(255, 228, 156), Rand.NextDouble() * W, Rand.NextDouble() * H * 0.8,
                        (Rand.NextDouble() - 0.5) * 6, 2 + Rand.NextDouble() * 5,
                        7 + Rand.NextDouble() * 8, Size, (Rand.NextDouble() - 0.5) * 16, 0.9)
        End If
    End Sub

    Private Sub SpawnFunAmbient(W As Double, H As Double)
        '娱乐：彩虹彩带 + 气球
        If Rand.NextDouble() < 0.22 AndAlso CountKind(9) < 8 Then
            Dim List = {Color.FromRgb(255, 122, 144), Color.FromRgb(255, 193, 94), Color.FromRgb(120, 205, 133), Color.FromRgb(96, 176, 255), Color.FromRgb(186, 140, 255)}
            Dim Size = (9 + Rand.NextDouble() * 8) * SizeScale
            NewParticle(9, List(Rand.Next(List.Length)), Rand.NextDouble() * W, H + 24,
                        (Rand.NextDouble() - 0.5) * 6, -(26 + Rand.NextDouble() * 22),
                        16 + Rand.NextDouble() * 12, Size, (Rand.NextDouble() - 0.5) * 14, 0.95)
        Else
            Dim Size = (4 + Rand.NextDouble() * 3.5) * SizeScale
            Dim Col = CType(New MyColor().FromHSL2(Rand.Next(360), 88, 62), Color)
            NewParticle(2, Col, Rand.NextDouble() * W, -12,
                        (Rand.NextDouble() - 0.5) * 22, 55 + Rand.NextDouble() * 55,
                        8 + Rand.NextDouble() * 8, Size, (Rand.NextDouble() - 0.5) * 320, 0.85)
        End If
    End Sub

    Private Sub SpawnMeteor(W As Double, H As Double)
        '流星：自画面上方向斜下角划过，金色（原神）或银白（星空）
        Dim FromLeft = Rand.NextDouble() < 0.5
        Dim AngleRad = (18 + Rand.NextDouble() * 16) * Math.PI / 180
        Dim Speed = 520 + Rand.NextDouble() * 300
        Dim VX = Math.Cos(AngleRad) * Speed * If(FromLeft, 1, -1)
        Dim VY = Math.Sin(AngleRad) * Speed
        Dim X = If(FromLeft, Rand.NextDouble() * W * 0.3 - 60, W * (0.7 + Rand.NextDouble() * 0.3) + 60)
        Dim Y = Rand.NextDouble() * H * 0.3 - 20
        Dim Col = If(FxStyle = 4, Color.FromRgb(255, 224, 150), Color.FromRgb(235, 242, 255))
        NewParticle(7, Col, X, Y, VX, VY, 0.75 + Rand.NextDouble() * 0.35,
                    (70 + Rand.NextDouble() * 55) * SizeScale, 0, 0.95)
    End Sub

    Private Sub SpawnBurst(X As Double, Y As Double)
        If FxCanvas Is Nothing OrElse BurstCount <= 0 Then Return
        '点击星星迸发：以点击点为中心向外扩散
        Dim ThemeCol = CType(New MyColor().FromHSL2(ColorHue, Math.Max(40, ColorSat), 72), Color)
        For i = 1 To BurstCount
            Dim Angle = Rand.NextDouble() * Math.PI * 2
            Dim Speed = 90 + Rand.NextDouble() * 170
            Dim Col = If(Rand.NextDouble() < 0.45, Color.FromRgb(255, 255, 255), ThemeCol)
            Dim Size = (4.5 + Rand.NextDouble() * 4) * SizeScale
            NewParticle(1, Col, X, Y, Math.Cos(Angle) * Speed, Math.Sin(Angle) * Speed - 30,
                        0.55 + Rand.NextDouble() * 0.55, Size, (Rand.NextDouble() - 0.5) * 260, 1)
        Next
    End Sub

    Private Sub NewParticle(Kind As Integer, Col As Color, X As Double, Y As Double, VX As Double, VY As Double,
                            Life As Double, Size As Double, Spin As Double, BaseOpacity As Double)
        Dim P As FxParticle = If(Spares.Count > 0, Spares(Spares.Count - 1), New FxParticle)
        If Spares.Count > 0 Then Spares.RemoveAt(Spares.Count - 1)
        P.Kind = Kind
        P.X = X : P.Y = Y : P.VX = VX : P.VY = VY
        P.Age = 0 : P.Life = Math.Max(0.3, Life)
        P.Size = Size : P.Spin = Spin : P.Phase = Rand.NextDouble() * Math.PI * 2
        P.BaseOpacity = BaseOpacity
        '形状类别变化时必须重建 Visual（对象池中可能复用了不同形状的粒子）
        Dim WantedShape = ShapeKindOf(Kind)
        If P.Visual Is Nothing OrElse P.VisualKind <> WantedShape Then
            If P.Visual IsNot Nothing AndAlso FxCanvas IsNot Nothing Then FxCanvas.Children.Remove(P.Visual)
            P.Visual = BuildVisual(P)
            P.VisualKind = WantedShape
        End If
        RefreshVisual(P, Col)
        If FxCanvas IsNot Nothing AndAlso FxCanvas.Parent IsNot Nothing Then
            Canvas.SetLeft(P.Visual, X)
            Canvas.SetTop(P.Visual, Y)
            P.Visual.Opacity = 0
            If FxCanvas.Children.IndexOf(P.Visual) < 0 Then FxCanvas.Children.Add(P.Visual)
            Actives.Add(P)
        End If
    End Sub

    Private Function BuildVisual(P As FxParticle) As FrameworkElement
        Dim Rotate As New RotateTransform(0)
        P.Rotate = Rotate
        Dim Element As FrameworkElement
        Select Case ShapeKindOf(P.Kind)
            Case 1 '星星
                Element = New System.Windows.Shapes.Path With {.Data = Geom(GeoStar, "M 0,-10 L 2.4,-2.4 L 10,0 L 2.4,2.4 L 0,10 L -2.4,2.4 L -10,0 L -2.4,-2.4 Z"), .Stretch = Stretch.Uniform}
            Case 2, 7 '彩带 / 流星拖尾
                Element = New System.Windows.Shapes.Rectangle
            Case 3 '樱瓣
                Element = New System.Windows.Shapes.Path With {.Data = Geom(GeoPetal, "M 0,-8 C 4.8,-4.5 4.8,2.5 0,8 C -4.8,2.5 -4.8,-4.5 0,-8 Z"), .Stretch = Stretch.Uniform}
            Case 4 '雪花
                Element = New System.Windows.Shapes.Path With {.Data = Geom(GeoSnow, "M 0,-10 L 1.1,0 L 0,10 L -1.1,0 Z M 8.66,-5 L 0.55,0.95 L -8.66,5 L -0.55,-0.95 Z M 8.66,5 L -0.55,0.95 L -8.66,-5 L 0.55,-0.95 Z"), .Stretch = Stretch.Uniform}
            Case 8 '枫叶
                Element = New System.Windows.Shapes.Path With {.Data = Geom(GeoMaple, "M 0,-10 L 1.8,-4.5 L 7.5,-6.5 L 4.6,-1.2 L 9.5,3.5 L 3.2,3.2 L 3.6,9 L 0,4.5 L -3.6,9 L -3.2,3.2 L -9.5,3.5 L -4.6,-1.2 L -7.5,-6.5 L -1.8,-4.5 Z"), .Stretch = Stretch.Uniform}
            Case 9 '气球
                Element = New System.Windows.Shapes.Path With {.Data = Geom(GeoBalloon, "M 0,-9 C 5,-9 6.5,-5 6.5,-2 C 6.5,2 3,5 1.2,6 L 2.2,8 L -2.2,8 L -1.2,6 C -3,5 -6.5,2 -6.5,-2 C -6.5,-5 -5,-9 0,-9 Z"), .Stretch = Stretch.Uniform}
            Case Else '光点 / 光晕
                Element = New System.Windows.Shapes.Ellipse
        End Select
        Element.RenderTransformOrigin = New Point(0.5, 0.5)
        Element.RenderTransform = Rotate
        Element.IsHitTestVisible = False
        Return Element
    End Function

    Private Sub RefreshVisual(P As FxParticle, Col As Color)
        Select Case ShapeKindOf(P.Kind)
            Case 1
                Dim Path = CType(P.Visual, System.Windows.Shapes.Path)
                Path.Fill = FrozenBrush(Col)
                Path.Width = P.Size : Path.Height = P.Size
            Case 2
                Dim Rect = CType(P.Visual, System.Windows.Shapes.Rectangle)
                Rect.Fill = FrozenBrush(Col)
                Rect.Width = P.Size * 0.55 : Rect.Height = P.Size * 1.35
                Rect.RadiusX = 1 : Rect.RadiusY = 1
            Case 7
                '流星：渐变拖尾沿飞行方向
                Dim Rect = CType(P.Visual, System.Windows.Shapes.Rectangle)
                Dim Tail As New LinearGradientBrush With {.StartPoint = New Point(0, 0.5), .EndPoint = New Point(1, 0.5)}
                Tail.GradientStops.Add(New GradientStop With {.Color = Color.FromArgb(0, Col.R, Col.G, Col.B), .Offset = 0})
                Tail.GradientStops.Add(New GradientStop With {.Color = Color.FromArgb(90, Col.R, Col.G, Col.B), .Offset = 0.72})
                Tail.GradientStops.Add(New GradientStop With {.Color = Col, .Offset = 1})
                Tail.Freeze()
                Rect.Fill = Tail
                Rect.Width = P.Size : Rect.Height = 2.6
                Rect.RadiusX = 1.3 : Rect.RadiusY = 1.3
                Dim Angle = Math.Atan2(P.VY, P.VX) * 180 / Math.PI
                If P.Rotate IsNot Nothing Then P.Rotate.Angle = Angle
            Case 3, 4, 8, 9
                Dim Path = CType(P.Visual, System.Windows.Shapes.Path)
                Path.Fill = FrozenBrush(Col)
                Path.Width = P.Size : Path.Height = P.Size
            Case Else
                Dim Ell = CType(P.Visual, System.Windows.Shapes.Ellipse)
                If P.Kind = 10 Then
                    '光晕：柔和径向渐变
                    Dim Glow As New RadialGradientBrush
                    Glow.GradientStops.Add(New GradientStop With {.Color = Col, .Offset = 0})
                    Glow.GradientStops.Add(New GradientStop With {.Color = Color.FromArgb(0, Col.R, Col.G, Col.B), .Offset = 1})
                    Glow.Freeze()
                    Ell.Fill = Glow
                Else
                    Ell.Fill = FrozenBrush(Col)
                End If
                Ell.Width = P.Size : Ell.Height = P.Size
        End Select
    End Sub

    Private Function FrozenBrush(Col As Color) As SolidColorBrush
        Dim Brush As New SolidColorBrush(Col)
        Brush.Freeze()
        Return Brush
    End Function

    Private Sub Recycle(P As FxParticle)
        If P.Visual IsNot Nothing AndAlso FxCanvas IsNot Nothing Then FxCanvas.Children.Remove(P.Visual)
        Spares.Add(P)
    End Sub

#End Region

#Region "输入"

    Private Sub OnPreviewMouseDown(sender As Object, e As MouseButtonEventArgs)
        Try
            If FxCanvas Is Nothing OrElse FxCanvas.Parent Is Nothing OrElse BurstCount <= 0 Then Return
            Dim Pos = e.GetPosition(FxCanvas)
            SpawnBurst(Pos.X, Pos.Y)
        Catch ex As Exception
            Logger.Error(ex, "幻星特效点击响应异常")
        End Try
    End Sub

#End Region

End Module
