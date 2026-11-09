#include <cstdint>
#include <cstdio>
#include <cstring>
#include <dxcapi.h>
#include <fstream>
#include <iostream>
#include <iterator>
#include <stdexcept>
#include <vector>

static void ok(HRESULT result) {
  if (FAILED(result))
    throw std::runtime_error("DXC call failed: " + std::to_string(result));
}
int main(int count, char **args) try {
  if (count != 4 && count != 5)
    throw std::runtime_error("Usage: aog-dxc-assemble input.ll "
                             "fresh-output.dxil template.dxil [--refresh]");
  std::ifstream input(args[1], std::ios::binary);
  if (!input)
    throw std::runtime_error("Input not readable");
  if (std::ifstream(args[2]).good())
    throw std::runtime_error("Fresh output required");
  std::vector<char> data((std::istreambuf_iterator<char>(input)), {});
  IDxcLibrary *library = nullptr;
  ok(DxcCreateInstance(CLSID_DxcLibrary, IID_PPV_ARGS(&library)));
  IDxcBlobEncoding *text = nullptr;
  ok(library->CreateBlobWithEncodingOnHeapCopy(data.data(), data.size(),
                                               CP_UTF8, &text));
  IDxcBlob *module = text;
  if (count == 5) {
    if (std::string(args[4]) != "--refresh")
      throw std::runtime_error("Unknown option");
    IDxcOptimizer *optimizer = nullptr;
    ok(DxcCreateInstance(CLSID_DxcOptimizer, IID_PPV_ARGS(&optimizer)));
    LPCWSTR passes[] = {L"-hlsl-dxilfinalize", L"-viewid-state",
                        L"-hlsl-dxilemit"};
    IDxcBlobEncoding *optimizerText = nullptr;
    auto optimized = optimizer->RunOptimizer(text, passes, 3, &module, &optimizerText);
    if (optimizerText && optimizerText->GetBufferSize())
      std::cerr.write((char *)optimizerText->GetBufferPointer(),
                      optimizerText->GetBufferSize());
    ok(optimized);
  }
  IDxcAssembler *assembler = nullptr;
  ok(DxcCreateInstance(CLSID_DxcAssembler, IID_PPV_ARGS(&assembler)));
  IDxcOperationResult *operation = nullptr;
  ok(assembler->AssembleToContainer(module, &operation));
  IDxcBlobEncoding *errors = nullptr;
  ok(operation->GetErrorBuffer(&errors));
  if (errors && errors->GetBufferSize())
    std::cerr.write((char *)errors->GetBufferPointer(),
                    errors->GetBufferSize());
  HRESULT status;
  ok(operation->GetStatus(&status));
  ok(status);
  IDxcBlob *result = nullptr;
  ok(operation->GetResult(&result));
  std::ifstream originalFile(args[3], std::ios::binary);
  if (!originalFile)
    throw std::runtime_error("Original container not readable");
  std::vector<char> original((std::istreambuf_iterator<char>(originalFile)),
                             {});
  auto word = [&](size_t offset) {
    if (offset > original.size() || original.size() - offset < 4)
      throw std::runtime_error("Truncated container");
    uint32_t value;
    std::memcpy(&value, original.data() + offset, 4);
    return value;
  };
  if (original.size() < 32 || std::memcmp(original.data(), "DXBC", 4) ||
      word(24) != original.size())
    throw std::runtime_error("Invalid original container");
  auto chunkCount = word(28);
  if (chunkCount > (original.size() - 32) / 4)
    throw std::runtime_error("Invalid chunk count");
  IDxcContainerBuilder *builder = nullptr;
  ok(DxcCreateInstance(CLSID_DxcContainerBuilder, IID_PPV_ARGS(&builder)));
  ok(builder->Load(result));
  bool rootFound = false;
  for (uint32_t i = 0; i < chunkCount; i++) {
    size_t offset = word(32 + i * 4);
    auto tag = word(offset);
    auto length = word(offset + 4);
    if (size_t(offset) + 8 + length > original.size())
      throw std::runtime_error("Truncated original chunk");
    if (tag != 0x30535452)
      continue;
    if (rootFound)
      throw std::runtime_error("Duplicate root signature");
    rootFound = true;
    IDxcBlobEncoding *root = nullptr;
    ok(library->CreateBlobWithEncodingOnHeapCopy(original.data() + offset + 8,
                                                 length, 0, &root));
    ok(builder->AddPart(tag, root));
  }
  if (!rootFound)
    throw std::runtime_error("Original root signature missing");
  IDxcOperationResult *built = nullptr;
  ok(builder->SerializeContainer(&built));
  ok(built->GetStatus(&status));
  ok(status);
  ok(built->GetResult(&result));
  IDxcValidator *validator = nullptr;
  ok(DxcCreateInstance(CLSID_DxcValidator, IID_PPV_ARGS(&validator)));
  IDxcOperationResult *validation = nullptr;
  ok(validator->Validate(result, DxcValidatorFlags_InPlaceEdit, &validation));
  IDxcBlobEncoding *validationErrors = nullptr;
  ok(validation->GetErrorBuffer(&validationErrors));
  if (validationErrors && validationErrors->GetBufferSize())
    std::cerr.write((char *)validationErrors->GetBufferPointer(),
                    validationErrors->GetBufferSize());
  ok(validation->GetStatus(&status));
  ok(status);
  // Exclusive creation also protects unreadable files and dangling symlinks.
  std::FILE *output = std::fopen(args[2], "wbx");
  if (!output)
    throw std::runtime_error("Could not create fresh output");
  auto written = std::fwrite(result->GetBufferPointer(), 1,
                             result->GetBufferSize(), output);
  int closed = std::fclose(output);
  if (written != result->GetBufferSize() || closed != 0)
    throw std::runtime_error("Output write failed");
  std::cout << "Assembled and validated " << result->GetBufferSize()
            << " bytes\n";
  return 0;
} catch (const std::exception &e) {
  std::cerr << e.what() << "\n";
  return 1;
}
