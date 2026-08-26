/*
 * remote_server.c
 * 순수 C언어 Win32 API 기반 초저지연 원격 제어 서버
 * - 캡처: Win32 GDI API (BitBlt)
 * - 통신: WinSock2 (TCP 포트 9000)
 * - 조작: SendInput API
 */

#define _WIN32_WINNT 0x0600
#include <winsock2.h>
#include <windows.h>
#include <ws2tcpip.h>
#include <stdio.h>
#include <stdlib.h>
#include <stdint.h>

#pragma comment(lib, "ws2_32.lib")
#pragma comment(lib, "gdi32.lib")
#pragma comment(lib, "user32.lib")

#define PORT 9000

#pragma pack(push, 1)
typedef struct {
    uint8_t type;  // 1: 마우스 이동, 2: 좌클릭, 3: 우클릭, 4: 키 입력
    int32_t x;
    int32_t y;
    uint32_t key;
} InputPacket;
#pragma pack(pop)

// 화면 캡처 함수 (BMP 메모리 바이트 반환)
BYTE* CaptureScreenBMP(int* outSize, int* outW, int* outH) {
    HDC hdcScreen = GetDC(NULL);
    HDC hdcMem = CreateCompatibleDC(hdcScreen);

    int w = GetSystemMetrics(SM_CXSCREEN);
    int h = GetSystemMetrics(SM_CYSCREEN);

    HBITMAP hBitmap = CreateCompatibleBitmap(hdcScreen, w, h);
    SelectObject(hdcMem, hBitmap);

    BitBlt(hdcMem, 0, 0, w, h, hdcScreen, 0, 0, SRCCOPY);

    BITMAPINFOHEADER bi;
    ZeroMemory(&bi, sizeof(bi));
    bi.biSize = sizeof(BITMAPINFOHEADER);
    bi.biWidth = w;
    bi.biHeight = -h; // Top-Down BMP
    bi.biPlanes = 1;
    bi.biBitCount = 24; // RGB 24bit
    bi.biCompression = BI_RGB;

    DWORD dwBmpSize = ((w * 24 + 31) / 32) * 4 * h;
    DWORD totalSize = sizeof(BITMAPFILEHEADER) + sizeof(BITMAPINFOHEADER) + dwBmpSize;

    BYTE* buffer = (BYTE*)malloc(totalSize);
    if (!buffer) {
        DeleteObject(hBitmap);
        DeleteDC(hdcMem);
        ReleaseDC(NULL, hdcScreen);
        return NULL;
    }

    BITMAPFILEHEADER bfh;
    bfh.bfType = 0x4D42; // "BM"
    bfh.bfSize = totalSize;
    bfh.bfReserved1 = 0;
    bfh.bfReserved2 = 0;
    bfh.bfOffBits = sizeof(BITMAPFILEHEADER) + sizeof(BITMAPINFOHEADER);

    memcpy(buffer, &bfh, sizeof(bfh));
    memcpy(buffer + sizeof(bfh), &bi, sizeof(bi));

    GetDIBits(hdcMem, hBitmap, 0, h, buffer + sizeof(bfh) + sizeof(bi), (BITMAPINFO*)&bi, DIB_RGB_COLORS);

    DeleteObject(hBitmap);
    DeleteDC(hdcMem);
    ReleaseDC(NULL, hdcScreen);

    *outSize = (int)totalSize;
    *outW = w;
    *outH = h;
    return buffer;
}

// 원격 입력 제어 실행
void ExecuteInput(InputPacket* pkt) {
    int screenW = GetSystemMetrics(SM_CXSCREEN);
    int screenH = GetSystemMetrics(SM_CYSCREEN);

    if (pkt->type == 1) { // 마우스 이동
        int normX = (pkt->x * 65535) / screenW;
        int normY = (pkt->y * 65535) / screenH;

        INPUT input = {0};
        input.type = INPUT_MOUSE;
        input.mi.dx = normX;
        input.mi.dy = normY;
        input.mi.dwFlags = MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_MOVE;
        SendInput(1, &input, sizeof(INPUT));
    } else if (pkt->type == 2) { // 마우스 좌클릭
        INPUT input[2] = {0};
        input[0].type = INPUT_MOUSE;
        input[0].mi.dwFlags = MOUSEEVENTF_LEFTDOWN;
        input[1].type = INPUT_MOUSE;
        input[1].mi.dwFlags = MOUSEEVENTF_LEFTUP;
        SendInput(2, input, sizeof(INPUT));
    } else if (pkt->type == 3) { // 마우스 우클릭
        INPUT input[2] = {0};
        input[0].type = INPUT_MOUSE;
        input[0].mi.dwFlags = MOUSEEVENTF_RIGHTDOWN;
        input[1].type = INPUT_MOUSE;
        input[1].mi.dwFlags = MOUSEEVENTF_RIGHTUP;
        SendInput(2, input, sizeof(INPUT));
    } else if (pkt->type == 4) { // 키보드 입력
        INPUT input[2] = {0};
        input[0].type = INPUT_KEYBOARD;
        input[0].ki.wVk = (WORD)pkt->key;
        input[1].type = INPUT_KEYBOARD;
        input[1].ki.wVk = (WORD)pkt->key;
        input[1].ki.dwFlags = KEYEVENTF_KEYUP;
        SendInput(2, input, sizeof(INPUT));
    }
}

// 수신 스레드 (클라이언트 조작 신호 처리)
DWORD WINAPI ClientRecvThread(LPVOID lpParam) {
    SOCKET clientSock = (SOCKET)lpParam;
    InputPacket pkt;

    while (1) {
        int bytesRead = recv(clientSock, (char*)&pkt, sizeof(InputPacket), 0);
        if (bytesRead <= 0) break;
        if (bytesRead == sizeof(InputPacket)) {
            ExecuteInput(&pkt);
        }
    }
    return 0;
}

int main() {
    WSADATA wsaData;
    if (WSAStartup(MAKEWORD(2, 2), &wsaData) != 0) {
        printf("[ERROR] WinSock 초기화 실패\n");
        return 1;
    }

    SOCKET listenSock = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
    if (listenSock == INVALID_SOCKET) {
        printf("[ERROR] 소켓 생성 실패\n");
        WSACleanup();
        return 1;
    }

    struct sockaddr_in serverAddr;
    serverAddr.sin_family = AF_INET;
    serverAddr.sin_addr.s_addr = INADDR_ANY;
    serverAddr.sin_port = htons(PORT);

    if (bind(listenSock, (struct sockaddr*)&serverAddr, sizeof(serverAddr)) == SOCKET_ERROR) {
        printf("[ERROR] 바인드 실패 (포트 %d 사용 중)\n", PORT);
        closesocket(listenSock);
        WSACleanup();
        return 1;
    }

    listen(listenSock, 1);
    printf("==================================================\n");
    printf("  C언어 Win32 초저지연 원격 서버가 시작되었습니다.\n");
    printf("  포트: %d | CCTV 스트리밍 & 원격 입력 대기 중...\n", PORT);
    printf("==================================================\n");

    while (1) {
        SOCKET clientSock = accept(listenSock, NULL, NULL);
        if (clientSock == INVALID_SOCKET) continue;

        printf("[INFO] 클라이언트가 연결되었습니다.\n");
        CreateThread(NULL, 0, ClientRecvThread, (LPVOID)clientSock, 0, NULL);

        // 프레임 전송 루프
        while (1) {
            int bmpSize = 0, w = 0, h = 0;
            BYTE* bmpBuffer = CaptureScreenBMP(&bmpSize, &w, &h);
            if (!bmpBuffer) break;

            // Header 전송 (프레임 크기 4바이트)
            int sendLen = send(clientSock, (char*)&bmpSize, sizeof(int), 0);
            if (sendLen <= 0) {
                free(bmpBuffer);
                break;
            }

            // Body 전송 (BMP 데이터)
            int totalSent = 0;
            while (totalSent < bmpSize) {
                int sent = send(clientSock, (char*)(bmpBuffer + totalSent), bmpSize - totalSent, 0);
                if (sent <= 0) break;
                totalSent += sent;
            }

            free(bmpBuffer);
            if (totalSent < bmpSize) break;

            // 약 30 FPS 전송 주기 (33ms)
            Sleep(33);
        }

        printf("[INFO] 클라이언트 연결 종료.\n");
        closesocket(clientSock);
    }

    closesocket(listenSock);
    WSACleanup();
    return 0;
}
