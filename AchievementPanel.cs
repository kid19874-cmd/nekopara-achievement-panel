using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

// Read-only local Steam cache viewer. No Steam API calls, injection or game writes.
namespace NekoparaAchievements {
  public sealed class BinaryKV {
    readonly BinaryReader r;
    int nodes;
    public BinaryKV(byte[] b) { r = new BinaryReader(new MemoryStream(b), Encoding.UTF8); }
    string Z() {
      var bytes = new List<byte>(); byte b;
      while ((b = r.ReadByte()) != 0) { bytes.Add(b); if (bytes.Count > 1048576) throw new InvalidDataException("KV string too long"); }
      return Encoding.UTF8.GetString(bytes.ToArray());
    }
    public Dictionary<string,object> Read(int depth = 0) {
      if (depth > 32) throw new InvalidDataException("KV nesting too deep");
      var d = new Dictionary<string,object>();
      while (r.BaseStream.Position < r.BaseStream.Length) {
        byte t = r.ReadByte(); if(t == 8) return d;
        if (++nodes > 100000) throw new InvalidDataException("Too many KV nodes");
        string k = Z(); object v;
        switch(t) {
          case 0: v = Read(depth+1); break;
          case 1: v = Z(); break;
          case 2: case 4: case 6: v = r.ReadUInt32(); break;
          case 3: v = r.ReadSingle(); break;
          case 7: v = r.ReadUInt64(); break;
          case 10: v = r.ReadInt64(); break;
          default: throw new InvalidDataException("Unsupported KV type " + t);
        }
        if(d.ContainsKey(k)) throw new InvalidDataException("Duplicate KV key");
        d.Add(k,v);
      }
      if(depth > 0) throw new EndOfStreamException("Incomplete KV object");
      return d;
    }
    public static Dictionary<string,object> Obj(Dictionary<string,object> d, string k) {
      object v; if (!d.TryGetValue(k,out v) || !(v is Dictionary<string,object>)) throw new InvalidDataException("Missing object: " + k);
      return (Dictionary<string,object>)v;
    }
  }

  public sealed class AchievementState {
    public uint Mask; public uint Account; public DateTime CacheTime;
    public Dictionary<string,object> Times;
    public bool Completed(int bit) { return (Mask & (1u << bit)) != 0; }
    public static AchievementState FromBytes(byte[] bytes) {
      var cache = BinaryKV.Obj(new BinaryKV(bytes).Read(), "cache");
      var group = BinaryKV.Obj(cache,"1");
      object m; if (!group.TryGetValue("data",out m) || !(m is uint)) throw new InvalidDataException("Missing achievement bitset");
      object times;
      return new AchievementState { Mask=(uint)m, Times=group.TryGetValue("AchievementTimes",out times) && times is Dictionary<string,object> ? (Dictionary<string,object>)times : new Dictionary<string,object>() };
    }
    public static AchievementState Load() {
      string steam;
      using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")) { steam=key == null ? null : key.GetValue("SteamPath") as string; }
      if(String.IsNullOrEmpty(steam)) throw new IOException("未找到 Steam 安装目录");
      uint account;
      using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\ActiveProcess")) {
        if(key==null || key.GetValue("ActiveUser")==null) throw new IOException("未找到当前 Steam 账号");
        account=unchecked((uint)Convert.ToInt64(key.GetValue("ActiveUser")));
      }
      if(account==0) throw new IOException("Steam 尚未登录");
      string path=Path.Combine(steam,"appcache","stats","UserGameStats_"+account+"_899970.bin");
      byte[] bytes;
      using(var fs=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)) {
        if(fs.Length > 4*1024*1024) throw new InvalidDataException("Cache too large");
        using(var ms=new MemoryStream()) { fs.CopyTo(ms); bytes=ms.ToArray(); }
      }
      var s=FromBytes(bytes); s.Account=account; s.CacheTime=File.GetLastWriteTime(path); return s;
    }
  }

  static class RoundedUI {
    public static GraphicsPath Shape(Rectangle rect,int radius) {
      int d=Math.Min(radius*2,Math.Min(rect.Width,rect.Height));var path=new GraphicsPath();
      if(d<=0)return path;
      path.AddArc(rect.Left,rect.Top,d,d,180,90);
      path.AddArc(rect.Right-d,rect.Top,d,d,270,90);
      path.AddArc(rect.Right-d,rect.Bottom-d,d,d,0,90);
      path.AddArc(rect.Left,rect.Bottom-d,d,d,90,90);path.CloseFigure();return path;
    }
    public static void ClipWindow(Form form,int radius) {
      if(form.ClientSize.Width<=0 || form.ClientSize.Height<=0)return;
      using(var path=Shape(new Rectangle(Point.Empty,form.ClientSize),radius)) {
        var old=form.Region;form.Region=new Region(path);if(old!=null)old.Dispose();
      }
    }
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
    public static void RemoveNativeFrame(Form form) {
      Action apply=()=>{try {
        int noBorder=unchecked((int)0xFFFFFFFE);DwmSetWindowAttribute(form.Handle,34,ref noBorder,4);
        int disabled=1;DwmSetWindowAttribute(form.Handle,2,ref disabled,4);
      }catch(DllNotFoundException){}catch(EntryPointNotFoundException){}};
      form.HandleCreated+=(s,e)=>apply();if(form.IsHandleCreated)apply();
    }
  }

  public class PanelWindow : Form {
    const int GridMargin=10, SizePx=64, Gap=8, Header=26, WidthPx=228, HeightPx=388;
    readonly string home=AppDomain.CurrentDomain.BaseDirectory;
    readonly string[] names={"第一次启动","用中文游玩吧！","用英文游玩吧！","用日文游玩吧！","摸摸巧克力","摸摸香草","摸摸时雨","摸摸枫","摸摸红豆","摸摸椰子","摸摸桂","会摇的……？","完成"};
    readonly string[] descriptions={"早安","将语言调成中文进行游戏","将语言调成英文进行游戏","将语言调成日文进行游戏","触碰了巧克力","触碰了香草","触碰了时雨","触碰了枫","触碰了红豆","触碰了椰子","触碰了桂","弹弹～","圣诞快乐！"};
    readonly Bitmap[] colored=new Bitmap[13], gray=new Bitmap[13];
    readonly ToolTip tip=new ToolTip();
    readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
    readonly ContextMenuStrip menu=new ContextMenuStrip();
    ColorModeWindow backgroundPicker;
    bool ColorPickerVisible {get{return backgroundPicker!=null && !backgroundPicker.IsDisposed && backgroundPicker.Visible;}}
    readonly ToolStripMenuItem followItem=new ToolStripMenuItem("跟随游戏窗口"), topItem=new ToolStripMenuItem("窗口置顶");
    AchievementState state;
    GuideWindow guide;
    int guideIndex=-1;
    string error="";
    bool following=true, userTop=true, dragging=false, hiddenForGame=false, firstTick=true;
    bool customBackground=false;
    bool palettePressed=false, colorDialogOpen=false, closing=false;
    bool? appliedTopMost=null;
    DateTime paletteOpenedAt,colorDialogOpenedAt;
    DateTime lastFrameSample=DateTime.MinValue;
    Point dragStart, formStart;
    int tick=0, hovered=-100;
    uint flashMask=0;
    DateTime flashUntil=DateTime.MinValue;
    IntPtr gameHandle=IntPtr.Zero;
    bool GameReady {get{return gameHandle!=IntPtr.Zero && IsWindow(gameHandle) && state!=null;}}
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left,Top,Right,Bottom; }
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll",SetLastError=true)] static extern IntPtr SendMessageTimeout(IntPtr h,uint message,IntPtr wParam,IntPtr lParam,uint flags,uint timeout,out IntPtr result);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attribute, out RECT r, int size);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int hgt,uint flags);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams { get { var p=base.CreateParams; p.ExStyle|=0x08000000; return p; } }
    protected override void WndProc(ref Message m) {
      if(m.Msg==0x21) { m.Result=new IntPtr(3); return; } // MA_NOACTIVATE: clicking panel never advances game dialogue.
      base.WndProc(ref m);
    }
    public PanelWindow() {
      Text="NEKOPARA Extra 成就图标";
      AutoScaleMode=AutoScaleMode.None;
      FormBorderStyle=FormBorderStyle.None; ClientSize=new Size(WidthPx,HeightPx);
      BackColor=Color.FromArgb(26,27,34); DoubleBuffered=true;
      RoundedUI.ClipWindow(this,12);
      RoundedUI.RemoveNativeFrame(this);
      ShowInTaskbar=true; StartPosition=FormStartPosition.Manual;
      Location=new Point(Screen.PrimaryScreen.WorkingArea.Right-WidthPx-20,70);
      try { Icon=Icon.ExtractAssociatedIcon(@"I:\SteamLibrary\steamapps\common\NEKOPARA Extra\nekopara_extra.exe"); } catch {}
      LoadSettings();
      for(int i=0;i<13;i++) {
        using(var b=new Bitmap(Path.Combine(home,"icons",i+"-icon.jpg"))) colored[i]=new Bitmap(b);
        using(var b=new Bitmap(Path.Combine(home,"icons",i+"-gray.jpg"))) gray[i]=new Bitmap(b);
      }
      tip.InitialDelay=350; tip.ReshowDelay=80; tip.AutoPopDelay=18000; tip.ShowAlways=true;
      followItem.Checked=following; followItem.CheckOnClick=true;
      followItem.CheckedChanged+=(s,e)=>{following=followItem.Checked; SaveSettings(); DockToGame(); Invalidate();};
      topItem.Checked=userTop; topItem.CheckOnClick=true;
      topItem.CheckedChanged+=(s,e)=>{userTop=topItem.Checked; SaveSettings();};
      menu.Items.Add(followItem); menu.Items.Add(topItem);
      menu.Items.Add("游戏",null,(s,e)=>FollowGameColor());
      menu.Items.Add("重新读取",null,(s,e)=>ReadState());
      menu.Items.Add("退出",null,(s,e)=>Close());
      ContextMenuStrip=menu;
      menu.Opening+=(s,e)=>timer.Stop();
      menu.Closed+=(s,e)=>ResumeRefresh();
      timer.Interval=500; timer.Tick+=(s,e)=>Pulse();
      Shown+=(s,e)=>{ReadState(); DockToGame(); timer.Start();};
      FormClosing+=(s,e)=>{closing=true;timer.Stop(); SaveSettings(); Log("closed");};
      Log("started; read-only Steam cache; 13 official icons; poll=2s");
    }
    Rectangle Cell(int i) {
      int col=i%3, row=i/3;
      if(i==12) col=1;
      return new Rectangle(GridMargin+col*(SizePx+Gap),Header+row*(SizePx+Gap),SizePx,SizePx);
    }
    void Log(string message) { try { File.AppendAllText(Path.Combine(home,"panel.log"),DateTime.Now.ToString("s")+" "+message+Environment.NewLine,Encoding.UTF8); } catch {} }
    void LoadSettings() {
      try {
        var a=File.ReadAllLines(Path.Combine(home,"settings.txt"));
        following=Boolean.Parse(a[0]); userTop=Boolean.Parse(a[1]);
        if(a.Length>4 && System.Text.RegularExpressions.Regex.IsMatch(a[4],"^#[0-9a-fA-F]{6}$"))BackColor=ColorTranslator.FromHtml(a[4]);
        customBackground=a.Length>5?Boolean.Parse(a[5]):a.Length>4 && !String.Equals(a[4],"#1A1B22",StringComparison.OrdinalIgnoreCase);
        int x=Int32.Parse(a[2]),y=Int32.Parse(a[3]);
        var rect=new Rectangle(x,y,WidthPx,HeightPx);
        foreach(var screen in Screen.AllScreens) if(screen.WorkingArea.IntersectsWith(rect)) {Location=new Point(x,y);break;}
      } catch {}
    }
    void SaveSettings() {
      try { string p=Path.Combine(home,"settings.txt"),tmp=p+".tmp";
        File.WriteAllLines(tmp,new[]{following.ToString(),userTop.ToString(),Left.ToString(),Top.ToString(),ColorTranslator.ToHtml(Color.FromArgb(BackColor.R,BackColor.G,BackColor.B)),customBackground.ToString()});
        if(File.Exists(p)) File.Replace(tmp,p,p+".bak"); else File.Move(tmp,p);
      } catch {}
    }
    void ReadState() {
      try {
        var next=AchievementState.Load();
        if(state!=null && state.Account==next.Account) { uint added=next.Mask & ~state.Mask; if(added!=0) {flashMask=added;flashUntil=DateTime.Now.AddSeconds(4);} }
        if(state==null || state.Mask!=next.Mask || state.Account!=next.Account) Log("cache mask="+next.Mask+"; complete="+Count(next.Mask)+"/13");
        state=next; error="";
      } catch(Exception ex) {
        string msg=ex.Message;
        if(error!=msg) Log("read unavailable: "+ex.GetType().Name);
        error=msg;
        // Never display another account's cached state after sign-out/account switch.
        state=null;
      }
      if(guide!=null && !guide.IsDisposed && guideIndex>=0)guide.SetAchievement(guideIndex,names[guideIndex],state==null?(bool?)null:state.Completed(guideIndex),colored[guideIndex]);
      hovered=-100; Invalidate();
    }
    static int Count(uint m) {int n=0; for(int i=0;i<13;i++) if((m&(1u<<i))!=0)n++; return n;}
    void Pulse() {
      if(colorDialogOpen || ColorPickerVisible || menu.Visible || closing)return;
      if(tick++%4==0)ReadState();
      DockToGame();
      var fg=GetForegroundWindow();
      bool shouldTop=userTop && (fg==gameHandle || fg==Handle || menu.Visible || ColorPickerVisible || (guide!=null && !guide.IsDisposed && fg==guide.Handle));
      if(appliedTopMost!=shouldTop){SetWindowPos(Handle,shouldTop ? new IntPtr(-1) : new IntPtr(-2),0,0,0,0,0x0001|0x0002|0x0010);appliedTopMost=shouldTop;}
      if(guide!=null && !guide.IsDisposed)guide.TopMost=shouldTop;
      if(flashMask!=0) { if(DateTime.Now>=flashUntil)flashMask=0; Invalidate(); }
      if(firstTick) { firstTick=false; SaveSettings(); }
    }
    void DockToGame() {
      if(gameHandle==IntPtr.Zero || !IsWindow(gameHandle)) {
        gameHandle=IntPtr.Zero;
        foreach(var process in Process.GetProcessesByName("nekopara_extra")) { using(process) {if(process.MainWindowHandle!=IntPtr.Zero) {gameHandle=process.MainWindowHandle;break;} } }
      }
      if(gameHandle==IntPtr.Zero) {if(hiddenForGame){Show();hiddenForGame=false;} return;}
      if(IsIconic(gameHandle) || !IsWindowVisible(gameHandle)) { if(!hiddenForGame && following) {Hide();hiddenForGame=true;} return; }
      if(hiddenForGame) {Show();hiddenForGame=false;}
      SyncFrameColor();
      if(!following || dragging)return;
      RECT r; if(!GetWindowRect(gameHandle,out r))return;
      RECT visible; try { if(DwmGetWindowAttribute(gameHandle,9,out visible,Marshal.SizeOf(typeof(RECT)))==0)r=visible; } catch {}
      var area=Screen.FromHandle(gameHandle).WorkingArea;
      int x=r.Right-2, y=r.Top; // Cover the visible frame seam by two physical pixels.
      if(x+WidthPx>area.Right) {
        if(r.Left-WidthPx>=area.Left)x=r.Left-WidthPx+2;
        else {
          // No side room: sit below the game if possible; otherwise use desktop edge.
          x=area.Right-WidthPx;
          if(r.Bottom+HeightPx<=area.Bottom)y=r.Bottom;
        }
      }
      x=Math.Max(area.Left,Math.Min(x,area.Right-WidthPx));
      y=Math.Max(area.Top,Math.Min(y,area.Bottom-HeightPx));
      if(Left!=x || Top!=y)SetWindowPos(Handle,IntPtr.Zero,x,y,0,0,0x0001|0x0004|0x0010);
    }
    void SyncFrameColor() {
      if(customBackground || gameHandle==IntPtr.Zero || !IsWindow(gameHandle) || IsIconic(gameHandle))return;
      if((DateTime.UtcNow-lastFrameSample).TotalSeconds<2)return;
      lastFrameSample=DateTime.UtcNow;
      RECT rect;if(!GetWindowRect(gameHandle,out rect))return;
      int width=rect.Right-rect.Left;if(width<240 || width>16384)return;
      Color color=SystemColors.Control;
      // Ask the game to paint only its non-client frame into a 32-pixel offscreen strip.
      // Unlike reading a screen DC, another app covering the game cannot contaminate the color.
      // Do not retain or save the strip. A bounded timeout protects the panel from a busy game.
      using(var strip=new Bitmap(width,32)) {
        bool painted=false;
        using(var graphics=Graphics.FromImage(strip)) {
          graphics.Clear(Color.Magenta);IntPtr dc=graphics.GetHdc();
          try {IntPtr ignored;painted=SendMessageTimeout(gameHandle,0x0317,dc,new IntPtr(2),0x0002,100,out ignored)!=IntPtr.Zero;}
          finally {graphics.ReleaseHdc(dc);}
        }
        var sample=strip.GetPixel(width/2,10);
        if(painted && sample.ToArgb()!=Color.Magenta.ToArgb())color=Color.FromArgb(sample.R,sample.G,sample.B);
      }
      if(color.ToArgb()!=BackColor.ToArgb()){BackColor=color;Invalidate();SaveSettings();Log("game frame background matched "+ColorTranslator.ToHtml(color));}
    }
    protected override void OnSizeChanged(EventArgs e) {base.OnSizeChanged(e);RoundedUI.ClipWindow(this,12);}
    protected override void OnPaint(PaintEventArgs e) {
      base.OnPaint(e); var g=e.Graphics;
      g.SmoothingMode=SmoothingMode.AntiAlias;
      g.InterpolationMode=InterpolationMode.HighQualityBicubic;
      // Compact status logo only: no cached timestamp or instructions in the header.
      using(var brush=new SolidBrush(GameReady?Color.FromArgb(77,216,139):Color.FromArgb(149,151,166)))g.FillEllipse(brush,10,7,11,11);
      if(GameReady)using(var pen=new Pen(Color.FromArgb(26,27,34),1.5f)){g.DrawLine(pen,13,12,15,14);g.DrawLine(pen,15,14,18,10);}
      Color[] wheel={Color.FromArgb(244,116,128),Color.FromArgb(245,190,97),Color.FromArgb(120,211,158),Color.FromArgb(106,187,232),Color.FromArgb(167,143,229),Color.FromArgb(227,134,194)};
      for(int c=0;c<wheel.Length;c++){double angle=c*Math.PI/3;using(var brush=new SolidBrush(wheel[c]))g.FillEllipse(brush,(float)(111+Math.Cos(angle)*4-2.5),(float)(13+Math.Sin(angle)*4-2.5),5,5);}
      using(var pen=new Pen(BackColor.GetBrightness()>0.65f?Color.FromArgb(40,40,45):Color.White,2.0f)) {g.DrawLine(pen,206,7,216,17);g.DrawLine(pen,216,7,206,17);}
      for(int i=0;i<13;i++) {
        var r=Cell(i); bool done=state!=null && state.Completed(i);
        var saved=g.Save();
        using(var rounded=RoundedUI.Shape(r,7)){g.SetClip(rounded);g.DrawImage(done?colored[i]:gray[i],r);}
        g.Restore(saved);
        using(var edge=RoundedUI.Shape(new Rectangle(r.X,r.Y,r.Width-1,r.Height-1),7))
        using(var pen=new Pen(Color.FromArgb(58,59,69)))g.DrawPath(pen,edge);
        if(state==null)using(var b=new SolidBrush(Color.FromArgb(232,172,87)))g.FillEllipse(b,r.Right-8,r.Bottom-8,5,5);
        if((flashMask&(1u<<i))!=0)using(var edge=RoundedUI.Shape(r,7))using(var p=new Pen(Color.FromArgb(242,204,121),2))g.DrawPath(p,edge);
      }
    }
    int Hit(Point p) {for(int i=0;i<13;i++)if(Cell(i).Contains(p))return i;if(p.Y<Header && p.X>199)return -2;if(p.Y<Header && p.X>=95 && p.X<=127)return -5;return p.Y<Header && p.X<31?-4:-1;}
    protected override void OnMouseMove(MouseEventArgs e) {
      base.OnMouseMove(e);
      if(dragging) {Location=new Point(formStart.X+Cursor.Position.X-dragStart.X,formStart.Y+Cursor.Position.Y-dragStart.Y);return;}
      int h=Hit(e.Location);Cursor=h>=0||h==-2||h==-5?Cursors.Hand:h==-4?Cursors.Default:e.Y<Header?Cursors.SizeAll:Cursors.Default;
      if(h==hovered)return;hovered=h;
      string text="";
      if(h>=0) {
        text=names[h]+"\n"+descriptions[h]+"\n";
        if(state==null)text+="状态未知：无法读取当前账号的 Steam 本地缓存。";
        else {
          text+=state.Completed(h)?"已完成":"未完成";
          object time;
          if(state.Completed(h) && state.Times.TryGetValue(h.ToString(),out time))try{text+=" · "+new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(Convert.ToDouble(time)).ToLocalTime().ToString("yyyy-MM-dd HH:mm");}catch{}
        }
      } else if(h==-2)text="关闭";
      else if(h==-5)text="背景颜色";
      else if(h==-4 && state!=null)text="已完成 "+Count(state.Mask)+" / 13";
      tip.SetToolTip(this,text);
    }
    protected override void OnMouseDown(MouseEventArgs e) {
      base.OnMouseDown(e);if(e.Button!=MouseButtons.Left)return;
      int h=Hit(e.Location);
      if(h==-2){Close();return;}
      if(h==-5){palettePressed=true;Capture=true;return;}
      if(h>=0){ShowGuide(h);return;}
      if(e.Y<Header){dragging=true;dragStart=Cursor.Position;formStart=Location;Capture=true;tip.Hide(this);}
    }
    void ChooseBackground() {
      if(closing || IsDisposed || colorDialogOpen)return;
      if(ColorPickerVisible){backgroundPicker.Close();return;}
      tip.Hide(this);
      timer.Stop();paletteOpenedAt=DateTime.UtcNow;
      backgroundPicker=new ColorModeWindow(customBackground);
      var area=Screen.FromControl(this).WorkingArea;
      backgroundPicker.Location=new Point(Math.Max(area.Left,Math.Min(Left+96,area.Right-backgroundPicker.Width)),Math.Max(area.Top,Math.Min(Top+24,area.Bottom-backgroundPicker.Height)));
      backgroundPicker.Picked+=(custom)=>{
        backgroundPicker.Close();
        BeginInvoke(new Action(()=>{if(closing || IsDisposed)return;if(custom)ChooseCustomBackground();else FollowGameColor();ResumeRefresh();}));
      };
      backgroundPicker.FormClosed+=(s,e)=>{Log("color options window closed; elapsedMs="+(int)(DateTime.UtcNow-paletteOpenedAt).TotalMilliseconds);ResumeRefresh();};
      backgroundPicker.Show(this);backgroundPicker.Activate();
      Log("color options window opened; automatic window refresh paused");
    }
    void FollowGameColor() {
      customBackground=false;lastFrameSample=DateTime.MinValue;SyncFrameColor();SaveSettings();
      Log("background mode=follow-game-frame");
    }
    void ChooseCustomBackground() {
      if(closing || colorDialogOpen)return;
      tip.Hide(this);
      colorDialogOpen=true;timer.Stop();colorDialogOpenedAt=DateTime.UtcNow;
      Log("color chooser opened; automatic window refresh paused");
      try {
        using(var chooser=new ColorDialog {Color=BackColor,FullOpen=true,AnyColor=true,SolidColorOnly=true}) {
          chooser.CustomColors=new[]{ColorTranslator.ToOle(Color.FromArgb(26,27,34)),ColorTranslator.ToOle(Color.FromArgb(47,52,67)),ColorTranslator.ToOle(Color.FromArgb(62,48,66)),ColorTranslator.ToOle(Color.FromArgb(38,58,58)),ColorTranslator.ToOle(Color.FromArgb(232,225,218)),ColorTranslator.ToOle(Color.FromArgb(234,220,229))};
          var result=chooser.ShowDialog(this);
          Log("color chooser closed result="+result+"; elapsedMs="+(int)(DateTime.UtcNow-colorDialogOpenedAt).TotalMilliseconds);
          if(result==DialogResult.OK){customBackground=true;BackColor=chooser.Color;SaveSettings();Invalidate();Log("panel background changed");}
        }
      } finally {colorDialogOpen=false;ResumeRefresh();}
    }
    void ResumeRefresh() {if(!closing && !IsDisposed && !colorDialogOpen && !ColorPickerVisible && !menu.Visible)timer.Start();}
    void ShowGuide(int index) {
      tip.Hide(this);
      guideIndex=index;
      if(guide==null || guide.IsDisposed)guide=new GuideWindow();
      guide.SetAchievement(index,names[index],state==null?(bool?)null:state.Completed(index),colored[index]);
      var area=Screen.FromControl(this).WorkingArea;
      int x=Right,y=Top;
      if(x+guide.Width>area.Right)x=Left-guide.Width;
      x=Math.Max(area.Left,Math.Min(x,area.Right-guide.Width));
      y=Math.Max(area.Top,Math.Min(y,area.Bottom-guide.Height));
      guide.Location=new Point(x,y);guide.Owner=this;guide.Show();guide.BringToFront();
      Log("guide opened bit="+index+"; source=SteamCommunity");
    }
    protected override void OnMouseUp(MouseEventArgs e) {
      base.OnMouseUp(e);
      if(palettePressed){palettePressed=false;Capture=false;if(e.Button==MouseButtons.Left && Hit(e.Location)==-5)BeginInvoke(new Action(ChooseBackground));return;}
      if(!dragging)return;dragging=false;Capture=false;
      if(Location!=formStart){followItem.Checked=false;SaveSettings();}
    }
    protected override void Dispose(bool disposing) {
      if(disposing) {timer.Dispose();tip.Dispose();menu.Dispose();if(backgroundPicker!=null && !backgroundPicker.IsDisposed)backgroundPicker.Dispose();foreach(var b in colored)if(b!=null)b.Dispose();foreach(var b in gray)if(b!=null)b.Dispose();}
      base.Dispose(disposing);
    }
  }

  public sealed class ColorModeWindow : Form {
    public event Action<bool> Picked;
    public ColorModeWindow(bool custom) {
      Text="颜色";FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;
      AutoScaleMode=AutoScaleMode.None;ClientSize=new Size(112,78);BackColor=Color.FromArgb(247,247,250);DoubleBuffered=true;TopMost=true;
      RoundedUI.ClipWindow(this,8);KeyPreview=true;
      RoundedUI.RemoveNativeFrame(this);
      KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Escape){e.Handled=true;Close();}};
      AddChoice("游戏",false,!custom,6);AddChoice("自选",true,custom,41);
    }
    void AddChoice(string text,bool custom,bool selected,int y) {
      var button=new Button {Text=(selected?"✓  ":"    ")+text,AccessibleName=text,Location=new Point(6,y),Size=new Size(100,31),FlatStyle=FlatStyle.Flat,BackColor=selected?Color.FromArgb(223,234,245):BackColor,ForeColor=Color.FromArgb(30,31,40),Font=new Font("Microsoft YaHei UI",10),TextAlign=ContentAlignment.MiddleLeft,Cursor=Cursors.Hand};
      button.FlatAppearance.BorderSize=0;button.Click+=(s,e)=>{var handler=Picked;if(handler!=null)handler(custom);};Controls.Add(button);
    }
    protected override void OnMouseUp(MouseEventArgs e) {base.OnMouseUp(e);if(e.Button==MouseButtons.Right)Close();}
  }

  public sealed class GuideWindow : Form {
    // Temporarily observe right-clicks anywhere, only while this guide is visible.
    // WH_MOUSE_LL runs in this process: no DLL is injected into the game.
    // Always forward events; never suppress another app's mouse input.
    delegate IntPtr MouseHook(int code,IntPtr message,IntPtr data);
    readonly MouseHook mouseCallback;
    IntPtr mouseHook=IntPtr.Zero;
    bool dismissQueued;
    bool moving;
    Point dragOrigin,windowOrigin;
    [DllImport("user32.dll",SetLastError=true)] static extern IntPtr SetWindowsHookEx(int id,MouseHook callback,IntPtr module,uint thread);
    [DllImport("user32.dll",SetLastError=true)] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
    public const string GuideUrl="https://steamcommunity.com/sharedfiles/filedetails/?id=2837077068";
    public const string HubUrl="https://steamcommunity.com/app/899970/guides/";
    readonly PictureBox picture=new PictureBox();
    readonly Label titleLabel=new Label(),statusLabel=new Label(),stepsLabel=new Label();
    public static string GuideText(int index) {
      if(index==0)return "从主菜单开始游戏，进入剧情。";
      if(index>=1 && index<=3) {
        string language=index==1?"中文":index==2?"English（英语）":"日本語（日语）";
        return "在窗口顶部点击 Text Language，选择"+language+"后进行游戏。";
      }
      if(index>=4 && index<=10) {
        string[] characters={"巧克力","香草","时雨","枫","红豆","椰子","桂"};
        return "等待"+characters[index-4]+"的立绘出现。点击右上角的手掌图标，再点击该角色（例如头部）进行抚摸。";
      }
      if(index==11)return "在设置的特效与声音页面启用特效，检查 E-mote 摇晃设置；返回角色立绘场景，按 P 触发效果。";
      if(index==12)return "阅读并完成全部剧情，直到游戏结尾。";
      throw new ArgumentOutOfRangeException("index");
    }
    static Label LabelAt(int x,int y,int w,int h,float size,Color color) {
      return new Label { Location=new Point(x,y),Size=new Size(w,h),ForeColor=color,BackColor=Color.Transparent,Font=new Font("Microsoft YaHei UI",size),UseCompatibleTextRendering=false };
    }
    public GuideWindow() {
      mouseCallback=ObserveMouse;
      Text="成就指南 · Steam 社区";FormBorderStyle=FormBorderStyle.None;
      StartPosition=FormStartPosition.Manual;ClientSize=new Size(340,346);AutoScaleMode=AutoScaleMode.None;
      BackColor=Color.FromArgb(30,31,40);ForeColor=Color.FromArgb(232,232,239);ShowInTaskbar=false;MaximizeBox=false;
      DoubleBuffered=true;RoundedUI.ClipWindow(this,14);
      RoundedUI.RemoveNativeFrame(this);
      picture.Location=new Point(18,18);picture.Size=new Size(64,64);picture.SizeMode=PictureBoxSizeMode.Zoom;Controls.Add(picture);
      titleLabel=LabelAt(96,20,227,33,13,ForeColor);Controls.Add(titleLabel);
      statusLabel=LabelAt(96,58,227,24,9,Color.FromArgb(149,151,166));Controls.Add(statusLabel);
      var heading=LabelAt(18,103,302,24,10,Color.FromArgb(238,188,178));heading.Text="解锁方法";Controls.Add(heading);
      stepsLabel=LabelAt(18,137,302,94,11,ForeColor);Controls.Add(stepsLabel);
      var source=LabelAt(18,241,304,37,9,Color.FromArgb(149,151,166));source.Text="来源：Steam 社区玩家指南\nNekopara Extra - 100% Achievements Guide";Controls.Add(source);
      var link=new LinkLabel {Location=new Point(18,287),Size=new Size(302,23),Text="查看 Steam 社区原帖 ↗",LinkColor=Color.FromArgb(147,205,238),ActiveLinkColor=Color.White,VisitedLinkColor=Color.FromArgb(147,205,238),Font=new Font("Microsoft YaHei UI",10)};
      link.LinkClicked+=(s,e)=>OpenCommunity(GuideUrl);Controls.Add(link);
      var more=new LinkLabel {Location=new Point(18,317),Size=new Size(302,20),Text="更多 Steam 社区指南 ↗",LinkColor=Color.FromArgb(153,155,170),Font=new Font("Microsoft YaHei UI",9)};
      more.LinkClicked+=(s,e)=>OpenCommunity(HubUrl);Controls.Add(more);
      KeyPreview=true;
      KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Escape){e.Handled=true;Close();}};
      AttachRightClickDismiss(this);
    }
    protected override void OnSizeChanged(EventArgs e) {base.OnSizeChanged(e);RoundedUI.ClipWindow(this,14);}
    protected override void OnPaint(PaintEventArgs e) {
      base.OnPaint(e);var graphics=e.Graphics;graphics.SmoothingMode=SmoothingMode.AntiAlias;
      using(var pen=new Pen(Color.White,2)){graphics.DrawLine(pen,316,7,326,17);graphics.DrawLine(pen,326,7,316,17);}
    }
    protected override void OnMouseDown(MouseEventArgs e) {
      base.OnMouseDown(e);if(e.Button!=MouseButtons.Left)return;
      if(e.Y<26 && e.X>305){Close();return;}
      if(e.Y<18){moving=true;dragOrigin=Cursor.Position;windowOrigin=Location;Capture=true;}
    }
    protected override void OnMouseMove(MouseEventArgs e) {
      base.OnMouseMove(e);
      if(moving)Location=new Point(windowOrigin.X+Cursor.Position.X-dragOrigin.X,windowOrigin.Y+Cursor.Position.Y-dragOrigin.Y);
      Cursor=e.Y<26 && e.X>305?Cursors.Hand:e.Y<18?Cursors.SizeAll:Cursors.Default;
    }
    protected override void OnMouseUp(MouseEventArgs e) {base.OnMouseUp(e);moving=false;Capture=false;}
    void Trace(string message) {
      try {File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"panel.log"),DateTime.Now.ToString("s")+" "+message+Environment.NewLine,Encoding.UTF8);}catch{}
    }
    void BeginMouseObservation() {
      if(mouseHook!=IntPtr.Zero || IsDisposed)return;
      dismissQueued=false;
      mouseHook=SetWindowsHookEx(14,mouseCallback,GetModuleHandle(null),0);
      Trace(mouseHook!=IntPtr.Zero?"guide outside-right-click listener active":"guide mouse listener failed error="+Marshal.GetLastWin32Error());
    }
    void EndMouseObservation() {
      if(mouseHook==IntPtr.Zero)return;
      UnhookWindowsHookEx(mouseHook);mouseHook=IntPtr.Zero;
      Trace("guide outside-right-click listener removed");
    }
    IntPtr ObserveMouse(int code,IntPtr message,IntPtr data) {
      if(code>=0 && message==new IntPtr(0x204) && Visible && !IsDisposed && !dismissQueued) {
        dismissQueued=true;
        try {BeginInvoke(new Action(()=>{if(!IsDisposed){Trace("guide dismissed by global right-click");Close();}}));}catch(InvalidOperationException){}
      }
      return CallNextHookEx(mouseHook,code,message,data);
    }
    protected override void OnShown(EventArgs e) {base.OnShown(e);BeginMouseObservation();}
    protected override void OnVisibleChanged(EventArgs e) {
      base.OnVisibleChanged(e);
      if(Visible && IsHandleCreated)BeginMouseObservation();else EndMouseObservation();
    }
    protected override void OnFormClosed(FormClosedEventArgs e) {EndMouseObservation();base.OnFormClosed(e);}
    protected override void Dispose(bool disposing) {if(disposing)EndMouseObservation();base.Dispose(disposing);}
    void AttachRightClickDismiss(Control control) {
      control.MouseUp+=(s,e)=>{if(e.Button==MouseButtons.Right)Close();};
      foreach(Control child in control.Controls)AttachRightClickDismiss(child);
    }
    protected override void WndProc(ref Message message) {
      // WM_NCRBUTTONUP also makes right-clicking the native title bar dismiss this guide.
      if(message.Msg==0xA5){Close();return;}
      base.WndProc(ref message);
    }
    void OpenCommunity(string url) {
      // Fixed, verified community URLs only. No identifiers, tokens, or local paths are transmitted.
      if(url!=GuideUrl && url!=HubUrl)return;
      try {Process.Start(new ProcessStartInfo(url){UseShellExecute=true});}catch(Exception ex){MessageBox.Show(this,"无法打开网页："+ex.Message,"Steam 社区");}
    }
    public void SetAchievement(int index,string name,bool? done,Image image) {
      titleLabel.Text=name;statusLabel.Text=done.HasValue?(done.Value?"已完成 · 可再次查看指南":"未完成"):"完成状态未知";
      statusLabel.ForeColor=done==true?Color.FromArgb(122,206,165):Color.FromArgb(176,177,191);
      stepsLabel.Text=GuideText(index);picture.Image=image;
      Text=name+" · 成就指南";
    }
  }

  static class Program {
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [STAThread] static void Main(string[] args) {
      if(args.Length>0 && args[0]=="--self-test") { SelfTest(args.Length>1?args[1]:"self-test.txt");return; }
      if(args.Length>1 && args[0]=="--snapshot") {
        try {var s=AchievementState.Load();var lines=new List<string>();lines.Add("mask="+s.Mask);lines.Add("cacheTime="+s.CacheTime.ToString("s"));for(int i=0;i<13;i++)lines.Add(i+"="+s.Completed(i));File.WriteAllLines(args[1],lines,Encoding.UTF8);}catch(Exception e){File.WriteAllText(args[1],"ERROR: "+e.Message);}return;
      }
      bool created;
      using(var mutex=new Mutex(true,@"Local\NekoparaExtraReadOnlyAchievementIcons",out created)) {
        if(!created)return;
        SetProcessDPIAware();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        try{Application.Run(new PanelWindow());}catch(Exception e){MessageBox.Show("成就显示器启动失败："+e.Message,"NEKOPARA Extra",MessageBoxButtons.OK,MessageBoxIcon.Error);}
      }
    }
    static void SelfTest(string output) {
      var report=new List<string>();
      foreach(uint mask in new uint[]{0,2051,8191}) {
        byte[] bytes;
        using(var ms=new MemoryStream())using(var w=new BinaryWriter(ms)) {
          Action<string> z=str=>{w.Write(Encoding.UTF8.GetBytes(str));w.Write((byte)0);};
          w.Write((byte)0);z("cache");w.Write((byte)0);z("1");w.Write((byte)2);z("data");w.Write(mask);w.Write((byte)8);w.Write((byte)8);bytes=ms.ToArray();
        }
        var s=AchievementState.FromBytes(bytes);int n=0;for(int i=0;i<13;i++){bool expected=(mask&(1u<<i))!=0;if(s.Completed(i)!=expected)throw new Exception("Bit mismatch");if(s.Completed(i))n++;}
        report.Add("PASS mask="+mask+" completed="+n);
        bool threw=false;try{AchievementState.FromBytes(new byte[]{0,99});}catch{threw=true;}
        if(!threw)throw new Exception("Truncated cache accepted");
      }
      bool missing=false;try{AchievementState.FromBytes(new byte[]{8});}catch{missing=true;}
      if(!missing)throw new Exception("Missing cache accepted");
      report.Add("PASS missing/truncated data remains unknown, not locked");
      var live=AchievementState.Load();report.Add("PASS live Steam cache parsed mask="+live.Mask);
      string home=AppDomain.CurrentDomain.BaseDirectory;
      for(int i=0;i<13;i++)foreach(string variant in new[]{"icon","gray"})using(var b=new Bitmap(Path.Combine(home,"icons",i+"-"+variant+".jpg"))){if(b.Width<32||b.Height<32)throw new Exception("Invalid icon");}
      report.Add("PASS 26 official Steam icon images decoded");
      for(int i=0;i<13;i++)if(String.IsNullOrEmpty(GuideWindow.GuideText(i)))throw new Exception("Missing guide");
      if(new Uri(GuideWindow.GuideUrl).Host!="steamcommunity.com")throw new Exception("Unexpected guide host");
      report.Add("PASS all 13 achievements mapped to Chinese Steam Community guide summaries");
      using(var round=RoundedUI.Shape(new Rectangle(0,0,228,388),12)) {
        if(round.IsVisible(0,0) || !round.IsVisible(114,194))throw new Exception("Invalid rounded panel path");
      }
      report.Add("PASS rounded frame excludes square corner and preserves content center");
      foreach(var color in new[]{Color.White,Color.Black,Color.FromArgb(82,45,120)}) {
        string html=ColorTranslator.ToHtml(Color.FromArgb(color.R,color.G,color.B));
        if(ColorTranslator.FromHtml(html).ToArgb()!=color.ToArgb())throw new Exception("Background color roundtrip failed");
      }
      report.Add("PASS white/black/custom RGB background color save-load roundtrip");
      File.WriteAllLines(output,report,Encoding.UTF8);
    }
  }
}
