# xyz 官网（静态站）

面向半导体及泛半导体设备制造商的产品官网，展示 xyz 装备上位机软件平台
（ECF 设备控制平台 / SECS/GEM 协议栈 / 设备驱动套件 / 数据与追溯）。

参考了 mingyeetech.com（业务结构）与 kxware.com（产品线组织与视觉风格）。

## 目录结构

```
website/
├── index.html        首页：Hero、我们做什么、产品线、核心能力、合作模式
├── platform.html     ECF 平台：六层架构图、机型插件、组件库、驱动、数据、技术栈
├── secs.html         SECS/GEM：HSMS-SS、SECS-II、GEM 五类采集器、E87/E84、冒烟测试
├── solutions.html    解决方案：机型四步适配、35021 案例、EAP 对接、仿真联调
├── about.html        关于我们 + 联系表单
├── css/site.css      全站样式（设计系统见 :root 变量）
├── js/site.js        导航 / 滚动入场 / 数字动画 / 表单演示
└── assets/           robot-hero.png（首页与案例页主视觉）、favicon.svg
```

## 本地预览

纯静态站，无构建依赖。任选其一：

```bash
# Python
python -m http.server 8080 -d D:/Code/website

# Node
npx serve D:/Code/website
```

浏览器打开 <http://127.0.0.1:8080>。直接双击 index.html 也可以打开。

## 部署

任意静态托管均可：Nginx / IIS / 阿里云 OSS 静态站 / Vercel / GitHub Pages。
把整个 `website/` 目录作为站点根目录上传即可。

## 上线前需要替换的占位内容

| 位置 | 占位内容 |
|---|---|
| 全站页脚邮箱 | `contact@xyz-semi.example.com`（全站共 5 处） |
| about.html 联系区 | 电话 `000-0000-0000`、地址「省 · 市 · 高新区科技园」 |
| about.html 表单 | 纯前端演示，未接后端；接入表单服务或改为mailto |
| 品牌名 `xyz` | 如需正式公司/产品名，全局替换 `xyz` 与 logo 文本 |

内容均基于 xyz.Framework 代码库的真实能力撰写（模块名、编号区间、超时参数、
机型 35021 等来自代码与文档），未虚构客户案例与经营数据。
