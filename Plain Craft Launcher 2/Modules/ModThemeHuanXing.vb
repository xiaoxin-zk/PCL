Imports System.Windows.Threading

''' <summary>
''' 幻星修改版：主题预设与特效调度。
''' 说明：官方开源仓库中的 主题ID→色相 映射位于被移除的闭源代码内，本模块在开源框架上
''' 重新实现全部主题的映射（0-13 为按官方观感还原的近似值），并新增 原神/星空/四季/娱乐
''' 四个预设，同时接通自定义主题（ID 14）的滑块参数。修改版内所有主题免费开放。
''' </summary>
Friend Module ModThemeHuanXing

#Region "主题 ID"

    Public Const ThemeGenshinId As Integer = 15 '原神 · 星穹夜空
    Public Const ThemeStarId As Integer = 16    '星空 · 深空
    Public Const ThemeSeasonId As Integer = 17  '四季 · 流转
    Public Const ThemeFunId As Integer = 18     '娱乐 · 彩虹

#End Region

    ''' <summary>
    ''' 在计算主题颜色前应用主题的色相参数；随后切换粒子特效风格。
    ''' </summary>
    Public Sub ApplyTheme(Id As Integer)
        Select Case Id
            Case 1
                SetHue(172, 78, 0, 0) '甜柠青
            Case 2
                SetHue(113, 70, 0, 0) '小草绿
            Case 3
                SetHue(48, 88, 0, 0) '菠萝黄
            Case 4
                SetHue(27, 48, -4, 0) '橡木棕
            Case 5
                SetHue(215, 8, -16, 0) '玄素黑
            Case 6
                SetHue(328, 78, 0, 0) '铁杆粉
            Case 7
                SetHue(282, 68, 0, 0) '神秘紫
            Case 8
                SetHue(43, 88, 0, 8) '秋仪金
            Case 9
                SetHue(24, 88, 0, 0) '活跃橙
            Case 10
                SetHue(4, 78, 0, 0) '跳票红
            Case 11
                SetHue(222, 76, -2, 0) '极客蓝
            Case 12
                '滑稽彩：色相循环（由主时钟每 250ms 调用 ThemeRefresh 推进），彩虹背景 + 娱乐特效
                ColorHue = (ColorHue + 3) Mod 361
                ColorSat = 85
                ColorLightAdjust = 0
                ColorHueTopbarDelta = 25
            Case 13
                SetHue(48, 92, 2, 8) '欧皇金
            Case 14
                '自定义：读取个性化页四个滑块的实时取值（20/90 为滑块的中性位）
                ColorHue = Settings.Get(Of Integer)("UiLauncherHue")
                ColorSat = Settings.Get(Of Integer)("UiLauncherSat")
                ColorLightAdjust = Settings.Get(Of Integer)("UiLauncherLight") - 20
                ColorHueTopbarDelta = Settings.Get(Of Integer)("UiLauncherDelta") - 90
            Case ThemeGenshinId
                SetHue(45, 72, 0, 14) '原神 · 星穹夜空 + 金色点缀
            Case ThemeStarId
                SetHue(232, 65, -2, 0) '星空 · 深空蓝
            Case ThemeSeasonId
                ApplySeasonTheme() '四季 · 随现实季节
            Case ThemeFunId
                '娱乐 · 彩虹：色相由计时器循环推进，此处只固定饱和度等
                ColorSat = 76
                ColorLightAdjust = 0
                ColorHueTopbarDelta = 25
            Case Else
                SetHue(210, 85, 0, 0) '龙猫蓝（默认）
        End Select
        If Id = ThemeFunId Then
            StartRainbow()
        Else
            StopRainbow()
        End If
        ModThemeFx.FxStyle = StyleFor(Id)
        If Id = ThemeSeasonId Then ModThemeFx.SeasonMode = SeasonNow()
        ModThemeFx.FxRefresh()
        If Id = ThemeFunId OrElse Id = 12 Then ModThemeFx.RainbowBgTick()
        ApplyCardBorder(Id)
    End Sub

#Region "卡片描边"

    ''' <summary>为主题设置卡片描边颜色：新增主题与四季着色以形成辨识度，官方主题保持透明（观感不变）。</summary>
    Private Sub ApplyCardBorder(Id As Integer)
        Try
            If Application.Current Is Nothing Then Return
            Dim Brush As Brush = Brushes.Transparent
            Select Case Id
                Case ThemeGenshinId : Brush = FrozenBorder(Color.FromArgb(175, 205, 173, 98)) '原神 · 鎏金
                Case ThemeStarId : Brush = FrozenBorder(Color.FromArgb(150, 104, 126, 188)) '星空 · 深蓝
                Case ThemeSeasonId
                    Select Case SeasonNow()
                        Case 1 : Brush = FrozenBorder(Color.FromArgb(140, 242, 170, 192)) '春 · 樱粉
                        Case 2 : Brush = FrozenBorder(Color.FromArgb(140, 126, 208, 232)) '夏 · 水色
                        Case 3 : Brush = FrozenBorder(Color.FromArgb(140, 238, 168, 82)) '秋 · 枫橙
                        Case Else : Brush = FrozenBorder(Color.FromArgb(140, 170, 202, 238)) '冬 · 霜蓝
                    End Select
                Case ThemeFunId, 12
                    Brush = ModThemeFx.MakeRainbowBorder(ColorHue) '娱乐 / 滑稽彩 · 彩虹
            End Select
            Application.Current.Resources("ColorBrushCardBorderFx") = Brush
        Catch ex As Exception
            Logger.Error(ex, "主题卡片描边更新失败")
        End Try
    End Sub

    Private Function FrozenBorder(Col As Color) As SolidColorBrush
        Dim Brush As New SolidColorBrush(Col)
        Brush.Freeze()
        Return Brush
    End Function

#End Region

    Private Sub SetHue(Hue As Integer, Sat As Integer, LightAdjust As Integer, TopbarDelta As Integer)
        ColorHue = Hue
        ColorSat = Sat
        ColorLightAdjust = LightAdjust
        ColorHueTopbarDelta = TopbarDelta
    End Sub

    ''' <summary>主题对应的特效风格。</summary>
    Private Function StyleFor(Id As Integer) As Integer
        Select Case Id
            Case ThemeGenshinId : Return 4
            Case ThemeStarId : Return 2
            Case ThemeSeasonId : Return 3
            Case ThemeFunId, 12 : Return 5
            Case Else : Return 0
        End Select
    End Function

#Region "四季"

    ''' <summary>当前季节：1 春 2 夏 3 秋 4 冬（按系统日期的月份自动判定）。</summary>
    Public Function SeasonNow() As Integer
        Select Case Date.Now.Month
            Case 3 To 5 : Return 1
            Case 6 To 8 : Return 2
            Case 9 To 11 : Return 3
            Case Else : Return 4
        End Select
    End Function

    Private Sub ApplySeasonTheme()
        Select Case SeasonNow()
            Case 1
                SetHue(130, 62, 0, 0) '春 · 新绿
            Case 2
                SetHue(203, 85, 0, 0) '夏 · 海蓝
            Case 3
                SetHue(33, 85, 0, 0) '秋 · 橙意
            Case Else
                SetHue(218, 26, -3, 0) '冬 · 霜白
        End Select
    End Sub

#End Region

#Region "彩虹循环"

    Private RainbowTimer As DispatcherTimer = Nothing

    Private Sub StartRainbow()
        If RainbowTimer IsNot Nothing Then Return
        RainbowTimer = New DispatcherTimer With {.Interval = TimeSpan.FromMilliseconds(250)}
        AddHandler RainbowTimer.Tick,
            Sub(s, e)
                Try
                    ColorHue = (ColorHue + 3) Mod 361
                    ThemeRefresh()
                Catch ex As Exception
                    Logger.Error(ex, "彩虹主题循环异常")
                End Try
            End Sub
        RainbowTimer.Start()
    End Sub

    Private Sub StopRainbow()
        If RainbowTimer Is Nothing Then Return
        RainbowTimer.Stop()
        RainbowTimer = Nothing
    End Sub

#End Region

End Module
