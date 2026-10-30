extends RefCounted
## 低计算量的界面主题工厂，颜色沿用 DesktopTool 的浅色蓝白风格。

## 构造统一的圆角控件背景。
func _box(color: Color, border: Color, radius: int = 7) -> StyleBoxFlat:
	var box := StyleBoxFlat.new()
	box.bg_color = color
	box.border_color = border
	box.set_border_width_all(1)
	box.set_corner_radius_all(radius)
	box.content_margin_left = 12
	box.content_margin_right = 12
	box.content_margin_top = 8
	box.content_margin_bottom = 8
	return box

## 创建原生控件主题，不承担数据或绘图业务。
func create_theme() -> Theme:
	var theme := Theme.new()
	theme.default_font_size = 14
	var font := SystemFont.new()
	font.font_names = PackedStringArray(["Microsoft YaHei", "Noto Sans CJK SC", "sans-serif"])
	theme.default_font = font
	for type in ["Label", "Button", "LineEdit", "TextEdit", "OptionButton", "CheckBox", "Tree", "ItemList", "PopupMenu", "SpinBox"]:
		theme.set_color("font_color", type, Color("202938"))
		theme.set_color("font_hover_color", type, Color("315ee7"))
		theme.set_color("font_pressed_color", type, Color("315ee7"))
		theme.set_color("font_focus_color", type, Color("315ee7"))
		theme.set_color("font_disabled_color", type, Color("a1a9b8"))
		theme.set_color("font_readonly_color", type, Color("576278"))
		theme.set_color("font_uneditable_color", type, Color("576278"))
	for type in ["LineEdit", "TextEdit"]:
		theme.set_stylebox("read_only", type, _box(Color("f9fafc"), Color("e7ebf1")))
	theme.set_stylebox("normal", "TextEdit", _box(Color.WHITE, Color("e7ebf1")))
	for type in ["Button", "OptionButton", "LineEdit", "SpinBox"]:
		theme.set_stylebox("normal", type, _box(Color.WHITE, Color("e7ebf1")))
		theme.set_stylebox("hover", type, _box(Color("f2f5fb"), Color("cad6ec")))
		theme.set_stylebox("pressed", type, _box(Color("edf1ff"), Color("b4c5ff")))
		theme.set_stylebox("disabled", type, _box(Color("f5f7fa"), Color("e7ebf1")))
		var focus := _box(Color(0, 0, 0, 0), Color("90a9ff"))
		focus.set_border_width_all(2)
		theme.set_stylebox("focus", type, focus)
	theme.set_stylebox("panel", "PanelContainer", _box(Color.WHITE, Color("e7ebf1"), 10))
	theme.set_stylebox("panel", "PopupMenu", _box(Color.WHITE, Color("dce2ed")))
	theme.set_stylebox("panel", "AcceptDialog", _box(Color.WHITE, Color("dce2ed")))
	theme.set_stylebox("panel", "Window", _box(Color.WHITE, Color("dce2ed")))
	theme.set_stylebox("panel", "ItemList", _box(Color("f9fafc"), Color("e7ebf1")))
	theme.set_stylebox("selected", "ItemList", _box(Color("edf1ff"), Color("d7e1ff")))
	theme.set_stylebox("selected_focus", "ItemList", _box(Color("edf1ff"), Color("b4c5ff")))
	theme.set_color("font_selected_color", "ItemList", Color("315ee7"))
	theme.set_stylebox("panel", "Tree", _box(Color("f9fafc"), Color("e7ebf1")))
	theme.set_stylebox("selected", "Tree", _box(Color("edf1ff"), Color("d7e1ff")))
	theme.set_stylebox("selected_focus", "Tree", _box(Color("edf1ff"), Color("b4c5ff")))
	theme.set_color("font_selected_color", "Tree", Color("315ee7"))
	theme.set_color("font_placeholder_color", "LineEdit", Color("98a2b3"))
	theme.set_constant("separation", "VBoxContainer", 8)
	theme.set_constant("separation", "HBoxContainer", 8)
	theme.set_constant("h_separation", "GridContainer", 8)
	theme.set_constant("v_separation", "GridContainer", 8)
	return theme
