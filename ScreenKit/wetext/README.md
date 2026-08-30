# WeText C++ ITN（语音后处理）

本目录随程序复制到输出根下 `wetext\`。

## 文件

| 路径 | 说明 |
|------|------|
| `processor_pipe.exe` | 常驻管道：stdin 一行 → stdout 一行 ITN 结果 |
| `processor_main.exe` | 官方 CLI（调试用） |
| `zh/itn/zh_itn_tagger.fst` | 中文 ITN tagger（**勿**用 enable_0_to_9，避免「一条→1条」） |
| `zh/itn/zh_itn_verbalizer.fst` | 中文 ITN verbalizer |

## 重新编译 C++ runtime

`third_party/WeTextProcessing` **不提交到本仓库**（见根目录 `.gitignore`）。需要重编时在仓库根目录自行克隆：

```bat
git clone https://github.com/wenet-e2e/WeTextProcessing.git third_party/WeTextProcessing
```

然后在 `third_party/WeTextProcessing/runtime`：

```bat
"D:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars64.bat"
cmake -B build-msvc -G "Visual Studio 18 2026" -A x64 -DBUILD_TESTING=OFF
cmake --build build-msvc --config Release --target processor_pipe -- /m:1
```

将 `build-msvc\bin\Release\processor_pipe.exe` 拷到本目录。

FST 可从 Python 包获取：

```bat
pip install wetext
copy %PYTHON%\Lib\site-packages\wetext\fsts\zh\itn\tagger.fst zh\itn\zh_itn_tagger.fst
copy %PYTHON%\Lib\site-packages\wetext\fsts\zh\itn\verbalizer.fst zh\itn\zh_itn_verbalizer.fst
```

注意：tagger 文件名须含 `zh_itn_`，runtime 靠路径判断语言。  
应用侧另有：量词前个位还原（`1条`→`一条`）、连续≥3 位中文数字才转阿拉伯数字、断句补「，」。

## 测试

```bat
echo 二点五平方电线| processor_pipe.exe --tagger=zh\itn\zh_itn_tagger.fst --verbalizer=zh\itn\zh_itn_verbalizer.fst
```
