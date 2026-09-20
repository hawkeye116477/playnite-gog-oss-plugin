project('NativeVcdiffPatch', 
        ['c', 'cpp'], 
         default_options : ['warning_level=3', 'cpp_std=c++20', 'buildtype=release', 'b_vscrt=static_from_buildtype', 'optimization=3'],
         license: 'MIT',
         version: '1.0')

third_party_dir = '../../third_party'

third_party_src = [
  third_party_dir + '/HDiffPatch/file_for_patch.c',
  third_party_dir + '/HDiffPatch/libHDiffPatch/HDiff/private_diff/limit_mem_diff/adler_roll.c',
  third_party_dir + '/HDiffPatch/libHDiffPatch/HPatch/hpatch_mt/_hcache_old_mt.c',
  third_party_dir + '/HDiffPatch/libHDiffPatch/HPatch/hpatch_mt/_hcache_window_old_mt.c',
  third_party_dir + '/HDiffPatch/libHDiffPatch/HPatch/hpatch_mt/_hinput_mt.c',
  third_party_dir + '/HDiffPatch/libHDiffPatch/HPatch/hpatch_mt/_houtput_mt.c',
  third_party_dir + '/HDiffPatch/libHDiffPatch/HPatch/hpatch_mt/_hpatch_mt.c',
  third_party_dir + '/HDiffPatch/libHDiffPatch/HPatch/hpatch_mt/hpatch_mt.c',
  third_party_dir + '/HDiffPatch/libHDiffPatch/HPatch/patch.c',
  third_party_dir + '/HDiffPatch/libParallel/parallel_import_c.c',
  third_party_dir + '/HDiffPatch/vcdiff_wrapper/vcpatch_wrapper.c',
]

src = [
  'dllmain.cpp',
]

sources = third_party_src + src


cpp_args = []
if get_option('buildtype') == 'debug'
    cpp_args += ['-DDEBUG_BUILD', 'D_DEBUG']
elif get_option('buildtype') == 'release'
    cpp_args += ['-DRELEASE_BUILD', '-DNDEBUG']
endif


compiler = meson.get_compiler('cpp')
if compiler.get_id() == 'msvc'
    cpp_args += ['-D_CRT_SECURE_NO_WARNINGS']
endif


NativeVcdiffPatch_lib = shared_library('NativeVcdiffPatch',
                                  sources: sources,
                                  cpp_args: cpp_args,
                                  name_prefix: '',
                                  include_directories: [third_party_dir]
                                  )
