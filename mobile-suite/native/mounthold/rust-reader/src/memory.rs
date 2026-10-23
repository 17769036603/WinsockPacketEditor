use crate::model::{MemoryRange, ProbeError};
use std::collections::{HashMap, VecDeque};
use std::fs::File;
use std::os::unix::fs::FileExt;

pub const PAGE_SIZE: u64 = 4096;
const DEFAULT_CACHE_PAGES: usize = 1024;

/// Read-only /proc/PID/mem reader with a per-sample bounded page cache.
/// A new instance is opened for every poll so cached pages never cross frames.
pub struct MemoryReader {
    file: File,
    ranges: Vec<MemoryRange>,
    pages: HashMap<u64, Vec<u8>>,
    lru: VecDeque<u64>,
    max_pages: usize,
}

impl MemoryReader {
    pub fn open(pid: u32, ranges: Vec<MemoryRange>) -> Result<Self, ProbeError> {
        let file = File::open(format!("/proc/{pid}/mem"))
            .map_err(|error| ProbeError::from_io("open process memory", error))?;
        Ok(Self {
            file,
            ranges,
            pages: HashMap::new(),
            lru: VecDeque::new(),
            max_pages: DEFAULT_CACHE_PAGES,
        })
    }

    #[cfg(test)]
    pub fn from_test_file(file: File, ranges: Vec<MemoryRange>) -> Self {
        Self {
            file,
            ranges,
            pages: HashMap::new(),
            lru: VecDeque::new(),
            max_pages: DEFAULT_CACHE_PAGES,
        }
    }

    pub fn ranges(&self) -> &[MemoryRange] {
        &self.ranges
    }

    pub fn clear_cache(&mut self) {
        self.pages.clear();
        self.lru.clear();
    }

    pub fn contains(&self, address: u64, size: usize) -> bool {
        self.ranges
            .iter()
            .any(|range| range.contains(address, size))
    }

    pub fn read(&mut self, address: u64, size: usize) -> Result<Vec<u8>, ProbeError> {
        if !self.contains(address, size) {
            return Err(ProbeError::read(format!(
                "memory span 0x{address:x}+{size} is outside readable private maps"
            )));
        }
        let end = address
            .checked_add(size as u64)
            .ok_or_else(|| ProbeError::read("memory span overflow"))?;
        let mut cursor = address;
        let mut result = Vec::with_capacity(size);
        while cursor < end {
            let page_address = cursor & !(PAGE_SIZE - 1);
            self.ensure_page(page_address)?;
            let offset = (cursor - page_address) as usize;
            let amount = ((PAGE_SIZE as usize) - offset).min((end - cursor) as usize);
            let piece = self
                .pages
                .get(&page_address)
                .ok_or_else(|| ProbeError::read("page cache lost a loaded page"))?
                .get(offset..offset + amount)
                .ok_or_else(|| ProbeError::read("requested bytes exceed the cached page"))?
                .to_vec();
            self.touch(page_address);
            result.extend_from_slice(&piece);
            cursor += amount as u64;
        }
        Ok(result)
    }

    pub fn read_direct(&self, address: u64, size: usize) -> Result<Vec<u8>, ProbeError> {
        if !self.contains(address, size) {
            return Err(ProbeError::read(format!(
                "scan span 0x{address:x}+{size} is outside readable private maps"
            )));
        }
        let mut result = vec![0u8; size];
        read_exact_at(&self.file, &mut result, address)?;
        Ok(result)
    }

    fn ensure_page(&mut self, page_address: u64) -> Result<(), ProbeError> {
        if self.pages.contains_key(&page_address) {
            self.touch(page_address);
            return Ok(());
        }
        if !self.contains(page_address, PAGE_SIZE as usize) {
            return Err(ProbeError::read(format!(
                "page 0x{page_address:x} is not fully contained in a readable map"
            )));
        }
        let mut bytes = vec![0u8; PAGE_SIZE as usize];
        read_exact_at(&self.file, &mut bytes, page_address)?;
        self.pages.insert(page_address, bytes);
        self.lru.push_back(page_address);
        while self.pages.len() > self.max_pages {
            if let Some(oldest) = self.lru.pop_front() {
                self.pages.remove(&oldest);
            } else {
                break;
            }
        }
        Ok(())
    }

    fn touch(&mut self, page_address: u64) {
        if let Some(index) = self.lru.iter().position(|value| *value == page_address) {
            self.lru.remove(index);
        }
        self.lru.push_back(page_address);
    }
}

fn read_exact_at(file: &File, buffer: &mut [u8], offset: u64) -> Result<(), ProbeError> {
    let mut read = 0usize;
    while read < buffer.len() {
        match file.read_at(&mut buffer[read..], offset + read as u64) {
            Ok(0) => {
                return Err(ProbeError::read(format!(
                    "short process-memory read at 0x{:x}: got {read} of {} bytes",
                    offset,
                    buffer.len()
                )))
            }
            Ok(amount) => read += amount,
            Err(error) if error.kind() == std::io::ErrorKind::Interrupted => continue,
            Err(error) => return Err(ProbeError::from_process_memory_io(error)),
        }
    }
    Ok(())
}
