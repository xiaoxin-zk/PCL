Imports System.Windows
Imports System.Windows.Controls
Imports System.Windows.Input
Imports System.Windows.Media

''' <summary>
''' 幻星修改版：主题粒子特效层。
''' 挂载于主窗口背景图片之上、界面内容之下，提供氛围粒子与点击星星迸发特效。
''' 所有元素走对象池，帧驱动使用 CompositionTarget.Rendering；
''' 密度与大小可在个性化设置调节，并可整体关闭，低性能设备会自动降级。
''' 特效层的任何异常都不允许影响启动器本体运行。
''' </summary>
Friend Module ModThemeFx

#Region "内部类型"

    Private Class FxParticle
        Public Visual As FrameworkElement = Nothing
        ''' <summary>当前 Visual 对应的形状类别（-1 未创建），与 Kind 的形状映射不同时需重建。</summary>
        Public VisualKind As Integer = -1
        Public Kind As Integer '0 光点 1 星星 2 彩带 3 花瓣 4 雪花 5 落叶 6 流萤
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
    Private StarGeometry As Geometry = Nothing

    ''' <summary>当前特效风格：0 主题色光点，2 星空，3 四季，4 原神，5 娱乐。</summary>
    Public FxStyle As Integer = 0
    ''' <summary>四季子模式：1 春 2 夏 3 秋 4 冬。</summary>
    Public SeasonMode As Integer = 1

    Private TargetCount As Integer = 0
    Private BurstCount As Integer = 0
    Private SizeScale As Double = 1.0

    Private Function StarGeom() As Geometry
        If StarGeometry Is Nothing Then
            StarGeometry = Geometry.Parse("M 0,-10 L 2.4,-2.4 L 10,0 L 2.4,2.4 L 0,10 L -2.4,2.4 L -10,0 L -2.4,-2.4 Z")
            StarGeometry.Freeze()
        End If
        Return StarGeometry
    End Function

#End Region

#Region "对外接口"

    ''' <summary>按当前主题与设置刷新特效层（主题切换、特效设置变化时调用）。</summary>
    Public Sub FxRefresh()
        RunInUi(Sub() FxRefreshCore())
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
                Case 1 : TargetCount = 22 : BurstCount = 9
                Case 2 : TargetCount = 55 : BurstCount = 14
                Case Else : TargetCount = 110 : BurstCount = 22
            End Select
            If TargetCount <= 0 OrElse FrmMain Is Nothing OrElse Not FrmMain.IsLoaded Then
                Detach()
                Return
            End If
            If FxStyle <> LastStyle OrElse (FxStyle = 3 AndAlso SeasonMode <> LastSeason) Then
                ClearParticles()
                LastStyle = FxStyle
                LastSeason = SeasonMode
            End If
            Attach()
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
        End If
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
            If SlowTick > 120 AndAlso TargetCount > 22 Then
                TargetCount = 22
                If Not ThrottleHinted Then
                    ThrottleHinted = True
                    Hint("检测到运行帧率较低，已自动减少主题特效粒子；也可以在设置中关闭特效")
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
        End Select
        P.X += P.VX * Dt
        P.Y += P.VY * Dt
        '氛围粒子的边界环绕
        If P.Age < P.Life - 0.05 Then
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

    Private Sub SpawnMoteAmbient(W As Double, H As Double)
        '主题色光点：缓慢上浮，微幅摆动
        Dim Size = (2.5 + Rand.NextDouble() * 3) * SizeScale
        Dim Col = CType(New MyColor().FromHSL2(ColorHue + Rand.Next(-10, 11), ColorSat, 62 + Rand.Next(-8, 9)), Color)
        NewParticle(0, Col, Rand.NextDouble() * W, H + 10, (Rand.NextDouble() - 0.5) * 8, -(8 + Rand.NextDouble() * 16),
                    14 + Rand.NextDouble() * 14, Size, (Rand.NextDouble() - 0.5) * 20, 0.45)
    End Sub

    Private Sub SpawnStarAmbient(W As Double, H As Double)
        '星空：近乎静止的星星，长生命周期闪烁
        Dim Size = (3 + Rand.NextDouble() * 4.5) * SizeScale
        Dim List = {Color.FromRgb(255, 255, 255), Color.FromRgb(207, 228, 255), Color.FromRgb(179, 157, 255)}
        NewParticle(1, List(Rand.Next(List.Length)), Rand.NextDouble() * W, Rand.NextDouble() * H,
                    (Rand.NextDouble() - 0.5) * 6, 2 + Rand.NextDouble() * 5,
                    9 + Rand.NextDouble() * 9, Size, (Rand.NextDouble() - 0.5) * 12, 0.9)
    End Sub

    Private Sub SpawnSeasonAmbient(W As Double, H As Double)
        Select Case SeasonMode
            Case 1 '春 · 樱瓣
                Dim List = {Color.FromRgb(255, 183, 197), Color.FromRgb(255, 224, 230), Color.FromRgb(255, 240, 245)}
                Dim Size = (3.5 + Rand.NextDouble() * 3) * SizeScale
                NewParticle(3, List(Rand.Next(List.Length)), Rand.NextDouble() * W, -12,
                            (Rand.NextDouble() - 0.5) * 14, 24 + Rand.NextDouble() * 22,
                            12 + Rand.NextDouble() * 10, Size, (Rand.NextDouble() - 0.5) * 90, 0.8)
            Case 2 '夏 · 流萤
                Dim Size = (2.5 + Rand.NextDouble() * 2.5) * SizeScale
                NewParticle(6, Color.FromRgb(232, 255, 148), Rand.NextDouble() * W, H * (0.2 + Rand.NextDouble() * 0.7),
                            0, 0, 10 + Rand.NextDouble() * 10, Size, 0, 0.85)
            Case 3 '秋 · 落叶
                Dim List = {Color.FromRgb(224, 146, 60), Color.FromRgb(199, 106, 47), Color.FromRgb(233, 189, 88)}
                Dim Size = (4 + Rand.NextDouble() * 3.5) * SizeScale
                NewParticle(5, List(Rand.Next(List.Length)), Rand.NextDouble() * W, -14,
                            (Rand.NextDouble() - 0.5) * 18, 42 + Rand.NextDouble() * 32,
                            9 + Rand.NextDouble() * 8, Size, (Rand.NextDouble() - 0.5) * 160, 0.85)
            Case Else '冬 · 初雪
                Dim Size = (2 + Rand.NextDouble() * 3.5) * SizeScale
                NewParticle(4, Color.FromRgb(255, 255, 255), Rand.NextDouble() * W, -12,
                            (Rand.NextDouble() - 0.5) * 16, 18 + Rand.NextDouble() * 24,
                            16 + Rand.NextDouble() * 14, Size, 0, 0.75)
        End Select
    End Sub

    Private Sub SpawnGenshinAmbient(W As Double, H As Double)
        '原神：升腾的元素光尘
        Dim List = {Color.FromRgb(255, 217, 143), Color.FromRgb(255, 236, 187), Color.FromRgb(126, 227, 217)}
        Dim Size = (2.5 + Rand.NextDouble() * 3.5) * SizeScale
        NewParticle(0, List(Rand.Next(List.Length)), Rand.NextDouble() * W, H + 10,
                    (Rand.NextDouble() - 0.5) * 10, -(12 + Rand.NextDouble() * 22),
                    12 + Rand.NextDouble() * 12, Size, (Rand.NextDouble() - 0.5) * 24, 0.6)
    End Sub

    Private Sub SpawnFunAmbient(W As Double, H As Double)
        '娱乐：彩虹彩带
        Dim Size = (4 + Rand.NextDouble() * 3.5) * SizeScale
        Dim Col = CType(New MyColor().FromHSL2(Rand.Next(360), 88, 62), Color)
        NewParticle(2, Col, Rand.NextDouble() * W, -12,
                    (Rand.NextDouble() - 0.5) * 22, 55 + Rand.NextDouble() * 55,
                    8 + Rand.NextDouble() * 8, Size, (Rand.NextDouble() - 0.5) * 320, 0.85)
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
        Dim P As FxParticle = If(Spares.Count > 0, Spares(Spares.Count - 1), SparesLastFallback())
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

    Private Function SparesLastFallback() As FxParticle
        Return New FxParticle
    End Function

    Private Function BuildVisual(P As FxParticle) As FrameworkElement
        Dim Rotate As New RotateTransform(0)
        P.Rotate = Rotate
        Dim Element As FrameworkElement
        Select Case P.Kind
            Case 1, 6 '星星
                Dim Path As New System.Windows.Shapes.Path With {.Data = StarGeom(), .Stretch = Stretch.Uniform}
                Element = Path
            Case 2 '彩带
                Element = New System.Windows.Shapes.Rectangle
            Case Else '光点 / 花瓣 / 雪花 / 落叶
                Element = New System.Windows.Shapes.Ellipse
        End Select
        Element.RenderTransformOrigin = New Point(0.5, 0.5)
        Element.RenderTransform = Rotate
        Element.IsHitTestVisible = False
        Return Element
    End Function

    Private Sub RefreshVisual(P As FxParticle, Col As Color)
        Dim Brush As New SolidColorBrush(Col)
        Brush.Freeze()
        Select Case P.Kind
            Case 1, 6
                Dim Path = CType(P.Visual, System.Windows.Shapes.Path)
                Path.Fill = Brush
                Path.Width = P.Size : Path.Height = P.Size
            Case 2
                Dim Rect = CType(P.Visual, System.Windows.Shapes.Rectangle)
                Rect.Fill = Brush
                Rect.Width = P.Size * 0.55 : Rect.Height = P.Size * 1.35
                Rect.RadiusX = 1 : Rect.RadiusY = 1
            Case Else
                Dim Ell = CType(P.Visual, System.Windows.Shapes.Ellipse)
                Ell.Fill = Brush
                Ell.Width = P.Size : Ell.Height = P.Size
        End Select
    End Sub

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
