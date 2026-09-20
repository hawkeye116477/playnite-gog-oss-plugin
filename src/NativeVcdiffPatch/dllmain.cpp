#include <string.h>
#include <stdlib.h>
#include <stdio.h>

#define _IS_NEED_PRINT_LOG 0
#define _IS_NEED_MAIN 0
#define _IS_NEED_CMDLINE 0
#define _IS_NEED_SFX 0
#define _IS_NEED_BSDIFF 0
#define _IS_NEED_VCDIFF 1
#define _IS_NEED_SINGLE_STREAM_DIFF 0
#define _IS_NEED_DIR_DIFF_PATCH 0
#define _IS_NEED_PRINT_PROGRESS 0

#define _IS_NEED_ALL_CompressPlugin 0
#define _IS_NEED_DEFAULT_CompressPlugin 0
#define _IS_NEED_ALL_ChecksumPlugin 0
#define _IS_NEED_DEFAULT_ChecksumPlugin 0

#include "HDiffPatch\hpatchz.c"

typedef void (__cdecl*ProgressCallback)(
    hpatch_StreamPos_t writtenBytes,
    hpatch_StreamPos_t totalBytes
);

typedef struct hpatch_TProgressStreamOutput {
    hpatch_TStreamOutput base;
    const hpatch_TStreamOutput *streamOutput;
    double time0;
    double progressR;
    unsigned int progress;
    ProgressCallback callback;
} hpatch_TProgressStreamOutput;


static void _progressStreamOutput_updateProgress(hpatch_TProgressStreamOutput *self, hpatch_StreamPos_t curPos) {
    unsigned int progress = (unsigned int) ((curPos * self->progressR) * 1000 + 0.5);
    progress = ((progress < 1000) && (((curPos < self->base.streamSize)))) ? progress : 1000;
    if (progress != self->progress) {
        double time1 = clock_s();
        hpatch_BOOL isEnd = (progress == 1000);
        if ((time1 >= self->time0 + 1.0 / 3) || isEnd) {
            self->progress = progress;
            self->time0 = time1;

            if (self->callback)
                self->callback(curPos, self->base.streamSize);
        }
    }
}

static hpatch_BOOL _progressStreamOutput_write(const struct hpatch_TStreamOutput *stream, hpatch_StreamPos_t writeToPos,
                                               const unsigned char *data, const unsigned char *data_end) {
    hpatch_TProgressStreamOutput *self = (hpatch_TProgressStreamOutput *) stream->streamImport;
    hpatch_BOOL result = self->streamOutput->write(self->streamOutput, writeToPos, data, data_end);
    if (result) {
        _progressStreamOutput_updateProgress(self, writeToPos + (hpatch_size_t) (data_end - data));
    }
    return result;
}

static hpatch_BOOL _progressStreamOutput_read_writed(const struct hpatch_TStreamOutput *stream,
                                                     hpatch_StreamPos_t readFromPos,
                                                     unsigned char *out_data, unsigned char *out_data_end) {
    hpatch_TProgressStreamOutput *self = (hpatch_TProgressStreamOutput *) stream->streamImport;
    return self->streamOutput->read_writed(self->streamOutput, readFromPos, out_data, out_data_end);
}

static const hpatch_TStreamOutput *_progressStreamInput_wrapper(hpatch_TProgressStreamOutput *self,
                                                                const hpatch_TStreamOutput *streamOutput,
                                                                ProgressCallback callback) {
    memset(self, 0, sizeof(*self));
    self->base.streamImport = self;
    self->base.streamSize = streamOutput->streamSize;
    self->base.write = _progressStreamOutput_write;
    self->base.read_writed = streamOutput->read_writed ? _progressStreamOutput_read_writed : 0;
    self->streamOutput = streamOutput;
    self->callback = callback;
    self->progress = -1;
    self->progressR = 1.0 / (streamOutput->streamSize ? streamOutput->streamSize : 1);
    _progressStreamOutput_updateProgress(self, 0);
    return &self->base;
}

int hpatchz(const char *oldFileName,
            const char *diffFileName,
            const char *outNewFileName,
            hpatch_BOOL isLoadOldAll,
            size_t patchCacheSize,
            hpatch_StreamPos_t diffDataOffset,
            hpatch_StreamPos_t diffDataSize,
            TPatchChecksumSet *checksumSet,
            size_t threadNum,
            size_t dec_threadNum,
            ProgressCallback progressCallback) {
    int result = HPATCH_SUCCESS;
    int patch_result = HPATCH_SUCCESS;
    hpatch_TProgressStreamOutput _progressStreamOutput;
    int _isInClear = hpatch_FALSE;
    double time0 = clock_s();

    _THDiffInfos diffInfos = {0};
    hpatch_TDecompress *decompressPlugin = &diffInfos._decompressPlugin;

    hpatch_TFileStreamOutput newData;
    hpatch_TFileStreamInput diffData;
    hpatch_TFileStreamInput oldData;

    const hpatch_TStreamInput *poldData = &oldData.base;
    const hpatch_TStreamOutput *pnewData = &newData.base;

    TByte *temp_cache = 0;
    size_t temp_cache_size = 0;

    hpatch_TFileStreamInput_init(&oldData);
    hpatch_TFileStreamInput_init(&diffData);
    hpatch_TFileStreamOutput_init(&newData);

    // open
    if ((0 == oldFileName) || (0 == strlen(oldFileName))) {
        mem_as_hStreamInput(&oldData.base, 0, 0);
    } else {
        check(hpatch_TFileStreamInput_open(&oldData, oldFileName),
              HPATCH_OPENREAD_ERROR, "open oldFile for read");
    }

    check(hpatch_TFileStreamInput_open(&diffData, diffFileName),
          HPATCH_OPENREAD_ERROR, "open diffFile for read");

    {
        // info
        int ret = _getHDiffInfos(&diffInfos, &diffData, dec_threadNum);

        if (ret != HPATCH_SUCCESS)
            check_on_error(ret);
    }
    if (decompressPlugin->open == 0)
        decompressPlugin = 0;

    if ((poldData->streamSize != diffInfos.diffInfo.oldDataSize) &&
        (diffInfos.diffInfo.oldDataSize != _kUnavailableSize)) {
        check_on_error(HPATCH_FILEDATA_ERROR);
    }

    check(hpatch_TFileStreamOutput_open(
              &newData,
              outNewFileName,
              diffInfos.diffInfo.newDataSize),
          HPATCH_OPENWRITE_ERROR,
          "open out newFile for write");

    hpatch_TFileStreamOutput_setRandomOut(&newData, hpatch_TRUE);

    if (diffInfos.isWindowDiff) {
        //alloc mem in _win_onDiffInfo
    } else {
        //alloc mem
        hpatch_StreamPos_t minCacheSize, betterCacheSize;
        getPatchMemSize(&minCacheSize, &betterCacheSize, isLoadOldAll, poldData->streamSize, patchCacheSize, 0);
        hpatch_StreamPos_t windowsSize = diffInfos.vcdiffInfo.maxSrcWindowsSize + diffInfos.vcdiffInfo.
                                         maxTargetWindowsSize;

        //get vcd patch mem size
        hpatch_StreamPos_t stWindowsSize = diffInfos.vcdiffInfo.maxSrcWindowsSize + diffInfos.vcdiffInfo.
                                           maxTargetWindowsSize;
        if (isLoadOldAll)
            betterCacheSize = stWindowsSize + kPatchCacheSize_bestmin;
        else if ((!diffInfos.vcdiffInfo.isHDiffzAppHead_a) || (diffInfos.vcdiffInfo.isHDiffzAppHead_window))
            // vcd default
            betterCacheSize = stWindowsSize + patchCacheSize;

        temp_cache = allocPatchMemCache(minCacheSize, betterCacheSize, &temp_cache_size);
        check(temp_cache, HPATCH_MEM_ERROR, "alloc cache memory");
    }
    pnewData = _progressStreamInput_wrapper(&_progressStreamOutput, pnewData, progressCallback);
    if (!vcpatch_with_cache(pnewData, poldData, &diffData.base, decompressPlugin,
                            checksumSet->isCheck_newRefData, temp_cache, temp_cache + temp_cache_size)) {
        patch_result = HPATCH_VCPATCH_ERROR;
    }

    if (patch_result != HPATCH_SUCCESS) {
        check(!oldData.fileError, HPATCH_FILEREAD_ERROR, "oldFile read");
        check(!diffData.fileError, HPATCH_FILEREAD_ERROR, "diffFile read");
        check_ferr(newData.fileError, HPATCH_FILEWRITE_ERROR, "out newFile write");
        if (decompressPlugin) check_dec(decompressPlugin->decError);
        check(hpatch_FALSE, patch_result, "patch run");
    }
    if (newData.out_length != newData.base.streamSize) {
        check_on_error(HPATCH_FILEDATA_ERROR);
    }

clear:
    check(hpatch_TFileStreamOutput_close(&newData),
          HPATCH_FILECLOSE_ERROR,
          "out newFile close");

    check(hpatch_TFileStreamInput_close(&diffData),
          HPATCH_FILECLOSE_ERROR,
          "diffFile close");

    check(hpatch_TFileStreamInput_close(&oldData),
          HPATCH_FILECLOSE_ERROR,
          "oldFile close");

    _free_mem(temp_cache);

    return result;
}

#if defined(_WIN32)
#define PATCH_API __declspec(dllexport)
#else
#define PATCH_API __attribute__((visibility("default")))
#endif

extern "C" PATCH_API int start_patching(const char *oldFileName,
                                        const char *diffFileName,
                                        const char *outNewFileName, size_t patchCacheSize,
                                        ProgressCallback progressCallback) {
    hpatch_BOOL isLoadOldAll = hpatch_FALSE;
    TPatchChecksumSet checksumSet = {0, hpatch_FALSE, hpatch_TRUE, hpatch_TRUE, hpatch_FALSE};
    return hpatchz(oldFileName, diffFileName, outNewFileName, isLoadOldAll, patchCacheSize, 0, 0, &checksumSet,
                   _THREAD_NUMBER_DEFAULT, _THREAD_NUMBER_DEFAULT, progressCallback);
}
