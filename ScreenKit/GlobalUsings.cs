// net48：无 ImplicitUsings，集中常用命名空间
global using System;
global using System.Collections.Generic;
global using System.IO;
global using System.Linq;
global using System.Threading;
global using System.Threading.Tasks;
global using System.Windows;
global using System.Windows.Controls;
global using System.Windows.Input;
global using System.Windows.Media;
global using System.Windows.Media.Imaging;

// UseWindowsForms 与 WPF 类型冲突时，默认走 WPF
global using Application = System.Windows.Application;
global using Clipboard = System.Windows.Clipboard;
global using MessageBox = System.Windows.MessageBox;
global using Cursors = System.Windows.Input.Cursors;
global using DataFormats = System.Windows.DataFormats;
global using DragDropEffects = System.Windows.DragDropEffects;
global using Color = System.Windows.Media.Color;
global using FontFamily = System.Windows.Media.FontFamily;
global using Point = System.Windows.Point;
