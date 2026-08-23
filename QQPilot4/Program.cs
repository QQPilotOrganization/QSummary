using IniParser.Model;
using QsummaryCore;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TextCopy;
using static System.Net.Mime.MediaTypeNames;
namespace QSummaryCore
{
    internal class Program
    {
       
        static bool autoFocusShouldRun = true;
        static readonly bool debug =false;
        static async Task Main(string[] args)
        {

            if (Environment.OSVersion.Version.Major >= 10)
            {
                Console.OutputEncoding = Encoding.UTF8;
            }

            DB db = new();
            Process? p=null;
            Process? p2 = null;

            try
            {
                p= Process.Start("ScaleToINI.exe");
                p2 = Process.Start("Umi-OCR.exe start");
            }
            catch (Exception e)
            {
                Log.Print(e.ToString(),Log.Stat.ERROR);
            }
            //GUIOperation.Init();
            //GUIOperation.Click(3, 3);
            ArrowLoad.StartLoading(ConsoleColor.Green, "正在初始化");
            //DockLog.Log2("正在初始化");
            GUIOperation.Init();
            IniParser.FileIniDataParser parser = new();
            IniData ini                 = parser.ReadFile("config.ini", new UTF8Encoding(false));
            KeyDataCollection general   = ini["general"];
            (int, int) size             = (int.Parse(general["width"]), int.Parse(general["height"]));
            p?.WaitForExit();
            float scale                 = float.Parse(general["scale"]);
            int scrollTries             = int.Parse(general["scroll"]);
            //bool withImage              = (general["withimage"].Equals("true", StringComparison.CurrentCultureIgnoreCase));
            //bool autoLogin              = (general["autologin"].Equals("true", StringComparison.CurrentCultureIgnoreCase));
            //int sendimagepossibility    = int.Parse(general["sendimagepossibility"]);
            //bool isVisionModel          = (general["isvisionmodel"].Equals("true", StringComparison.CurrentCultureIgnoreCase));
            bool ATDetect               = (general["atdetect"].Equals("true", StringComparison.CurrentCultureIgnoreCase));
            int tapTimes                =  int.Parse(general["tab_times"]);
            Console.ForegroundColor = ConsoleColor.Cyan;
            Log.Print(general["version"]);
            ArrowLoad.StopLoading();
            Console.ResetColor();

            Log.Print("初始化完成");

            string OSDescription = System.Runtime.InteropServices.RuntimeInformation.OSDescription;
            Console.ForegroundColor = ConsoleColor.Yellow;
            Log.Print(OSDescription);
            Console.ResetColor();

            Thread autoFocusThread = new(autoFocus);
            autoFocusThread.Start();




            Log.Print("自动聚焦功能已开启");



            size = ((int)(size.Item1 * scale), (int)(size.Item2 * scale));
            (int,int,int,int) positionRect=(0,0,size.Item1,size.Item2);

            // 聊天列表实际大小
            var chatListActualSize = Positions.ToActualSize(Positions.CHAT_LIST_BBOX_RELATIVE_SIZE, size);
            Log.Print( $"聊天列表实际大小: {chatListActualSize}");
            // 聊天区域实际大小
            var conversationActualSize = Positions.ToActualSize(Positions.CONVERSATION_BBOX_RELATIVE_SIZE, size);
            Log.Print( $"聊天区域实际大小: {conversationActualSize}");
            // 输入框实际大小
            var commentSectionActualSize = Positions.ToActualSize(Positions.COMMENT_SECTION_BBOX_RELATIVE_SIZE, size);
            Log.Print( $"输入框实际大小: {commentSectionActualSize}");
            // 发送按钮实际大小
            // 退出会话按钮实际大小
            var exitConversationActualSize = Positions.ToActualSize(Positions.EXIT_CONVERSATION_BBOX_RELATIVE_SIZE, size);
            Log.Print( $"退出会话按钮实际大小: {exitConversationActualSize}");
            // 发送图片按钮实际大小
            // @位置实际大小
            var atPlaceActualSize = Positions.ToActualSize(Positions.AT_PLACE_BBOX_RELATIVE_SIZE, size);
            Log.Print( $"@位置实际大小: {atPlaceActualSize}");
            // 拖拽起止位置
            var startDraggingAbsolutePosition = Positions.ToActualPoint(Positions.START_DRAGGING_RELATIVE_POSITION, size);
            var endDraggingAbsolutePosition = Positions.ToActualPoint(Positions.END_DRAGGING_RELATIVE_POSITION, size);
            Log.Print( $"开始拖拽位置: {startDraggingAbsolutePosition}");
            Log.Print( $"结束拖拽位置: {endDraggingAbsolutePosition}");
            // 聊天按钮和联系人按钮位置
            var chatButtonActualPosition = Positions.ToActualPoint(Positions.CHAT_BUTTON_RELATIVE_POSITION, size);
            Log.Print( $"聊天按钮实际位置: {chatButtonActualPosition}");
            var contactButtonActualPosition = Positions.ToActualPoint(Positions.CONTACT_BUTTON_RELATIVE_POSITION, size);
            Log.Print( $"联系人按钮实际位置: {contactButtonActualPosition}");
            // 取消按钮位置（未打印日志，按需添加）
            var cancelButtonActualPosition = Positions.ToActualPoint(Positions.CANCEL_BUTTON_RELATIVE_POSITION, size);
            // 上传图片和复制按钮可能区域
            var uploadImagePossibleActualSize = Positions.ToActualSize(Positions.UPLOAD_IMAGE_POSSIBLE_BBOX_RELATIVE_SIZE, size);
            var copyButtonPossibleActualSize = Positions.ToActualSize(Positions.COPY_BUTTON_POSSIBLE_BBOX_RELATIVE_SIZE, size);
            Log.Print( $"上传图片可能位置: {uploadImagePossibleActualSize}");
            Log.Print( $"复制按钮可能位置: {copyButtonPossibleActualSize}");
            //Answer? answer = null;

            //GUIOperation.Focus_();
            var NameActualSize = Positions.ToActualSize(Positions.NAME_POSSIBLE_BBOX_RELATIVE_SIZE, size);
            Log.Print($"群自定义名称实际大小: {NameActualSize}");
            //Image.CropImage("screenshot.png", "dst.png", NameActualSize.Item1, NameActualSize.Item2, NameActualSize.Item3, NameActualSize.Item4);
            OcrClient ocr = new();

            Log.Print("======================================================");
            Log.Print("");
            Log.Print("\t使用时请勿移动鼠标！");
            Log.Print("");
            Log.Print("======================================================");


            bool cancelled=false;
            Console.CancelKeyPress += (sender, e) =>
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Log.Print("\n结束运行");
                //DockLog.Exit();
                Console.ResetColor();
                // 设置 e.Cancel = true 可以阻止程序立即终止，
                // 允许执行清理逻辑后再退出
                cancelled = true;
                autoFocusShouldRun = false;
                autoFocusThread.Join();
                e.Cancel = true;
                throw new SystemException("Terminate");
            };

            while (! cancelled)
            {
                Console.Write("正在寻找新信息...\r");
                //DockLog.Log2("正在寻找新信息...");
                var chatList = Image.FullScreenShot();
                (uint,uint) contain=(0,0);
                if(ATDetect)
                {
                    contain = Image.ContainsRedDot(Image.Rect(atPlaceActualSize));
                }
                else
                {
                    contain = Image.ContainsRedDot(Image.Rect(chatListActualSize));
                }
                if(contain!=(0,0))
                {
                    Thread.Sleep(500); 
                    if (ATDetect)
                    {
                        contain = Image.ContainsRedDot(Image.Rect(atPlaceActualSize));
                    }
                    else
                    {
                        contain = Image.ContainsRedDot(Image.Rect(chatListActualSize));
                    }
                    if (contain == (0,0))
                    {
                        continue;
                    }
                    Console.ForegroundColor= ConsoleColor.Green;
                    Log.Print($"发现红点: {contain}");

                    Console.ResetColor();

                    GUIOperation.Click((int)contain.Item1,(int)contain.Item2);
                    Thread.Sleep(1000);
                    Image.Screenshot(NameActualSize.Item1, NameActualSize.Item2, NameActualSize.Item3, NameActualSize.Item4);
                    String groupName = (await ocr.RecognizeFileAsync("screenshot.png")).PlainText.Replace("/","-");
                    // groupName= //Uri.EscapeDataString(groupName); 
                    groupName=UrlSanitizer.SanitizeUrlSegment(groupName);
                    //Log.Print(startDraggingAbsolutePosition.Item1.ToString(), startDraggingAbsolutePosition.Item2, endDraggingAbsolutePosition.Item1, endDraggingAbsolutePosition.Item2);
                    GUIOperation.DragFromToSimple(startDraggingAbsolutePosition.Item1,startDraggingAbsolutePosition.Item2,endDraggingAbsolutePosition.Item1,endDraggingAbsolutePosition.Item2);
                    Thread.Sleep(500);
                    GUIOperation.GotoCenter(conversationActualSize);
                    Thread.Sleep(500);

                    Image.Screenshot(copyButtonPossibleActualSize);
                    Thread.Sleep(1000);
                    List<(uint x, uint y)> points = Image.FindTemplates("screenshot.png", "./copy.png",30,1);
                    if (points.Count == 0)
                    {
                        Log.Print("使用模板匹配查找复制按钮失败");
                        //DockLog.Log2("使用模板匹配查找复制按钮失败");
                        for (int i = 0; i < scrollTries * 2; i++)
                        {
                            Thread.Sleep(400);
                            GUIOperation.ScrollDown(480);

                        }
                        GUIOperation.ClickCenter(commentSectionActualSize);

                        for (int i = 0; i <tapTimes; i++)
                        {

                            GUIOperation.Tab();
                            Thread.Sleep(400);

                        }
                        GUIOperation.PressKey("enter");
                        Thread.Sleep(200);

                    }
                    else
                    {
                        Thread.Sleep(2000);
                        GUIOperation.Click((int)(points[0].x + copyButtonPossibleActualSize.Item1),(int)(points[0].y+copyButtonPossibleActualSize.Item2));
                        Thread.Sleep(200);

                    }

                    Clipboard pyperclip = new();
                    string chatContentStr=pyperclip.GetText()??"";
                    List<ChatContent> ChatContents = ConversationStyleExtract.ParseChatLog(chatContentStr);
                    SpinnerLoad.Start(ConsoleColor.Green,"收集");
                    //DockLog.Log2("等待语言模型生成答案");
                    db.Insert(groupName, ChatContents);
                    Log.Print(groupName);
                    GUIOperation.ClickCenter(commentSectionActualSize);
                    SpinnerLoad.Stop();
                    Thread.Sleep(100);
                    Log.Print("退出会话");


                    List<(uint x, uint y)> points2 =[];
                    do
                    {
                        GUIOperation.Click(chatButtonActualPosition.Item1 + (int)(100 * scale), chatButtonActualPosition.Item2 + (int)(80 * scale));
                        Thread.Sleep(800);
                        Image.Screenshot(copyButtonPossibleActualSize);
                        Thread.Sleep(1000);
                        points2 = Image.FindTemplates("screenshot.png", "./copy.png", 30, 1);
                    } while (points2.Count!=0);

                }
                else
                {
                    Thread.Sleep(2000);
                }
            }
            autoFocusShouldRun = false;
            autoFocusThread.Join();
        }
        static void autoFocus()
        {
            while(autoFocusShouldRun)
            {
                if(! debug)
                { 
                    GUIOperation.Focus_();
                }
                Thread.Sleep(4000);
            }
        }
    }
}
