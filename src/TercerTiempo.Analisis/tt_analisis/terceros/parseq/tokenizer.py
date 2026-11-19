# Tokenizer and init_weights from strhub (https://github.com/baudm/parseq), Apache-2.0, Copyright 2022 Darwin Bautista.
# Only what the PARSeq model needs to run without PyTorch Lightning.

from typing import Optional, Sequence

import torch
from torch import Tensor, nn
from torch.nn.utils.rnn import pad_sequence


class Tokenizer:
    BOS = '[B]'
    EOS = '[E]'
    PAD = '[P]'

    def __init__(self, charset: str) -> None:
        specials_first = (self.EOS,)
        specials_last = (self.BOS, self.PAD)
        self._itos = specials_first + tuple(charset) + specials_last
        self._stoi = {s: i for i, s in enumerate(self._itos)}
        self.eos_id, self.bos_id, self.pad_id = [self._stoi[s] for s in specials_first + specials_last]

    def __len__(self):
        return len(self._itos)

    def encode(self, labels: list[str], device: Optional[torch.device] = None) -> Tensor:
        batch = [
            torch.as_tensor([self.bos_id] + [self._stoi[c] for c in y] + [self.eos_id], dtype=torch.long, device=device)
            for y in labels
        ]
        return pad_sequence(batch, batch_first=True, padding_value=self.pad_id)

    def id_of(self, token: str) -> int:
        return self._stoi[token]


def init_weights(module: nn.Module, name: str = '', exclude: Sequence[str] = ()):
    """Initialize the weights using the typical initialization schemes used in SOTA models."""
    if any(map(name.startswith, exclude)):
        return
    if isinstance(module, nn.Linear):
        nn.init.trunc_normal_(module.weight, std=0.02)
        if module.bias is not None:
            nn.init.zeros_(module.bias)
    elif isinstance(module, nn.Embedding):
        nn.init.trunc_normal_(module.weight, std=0.02)
        if module.padding_idx is not None:
            module.weight.data[module.padding_idx].zero_()
    elif isinstance(module, nn.Conv2d):
        nn.init.kaiming_normal_(module.weight, mode='fan_out', nonlinearity='relu')
        if module.bias is not None:
            nn.init.zeros_(module.bias)
    elif isinstance(module, (nn.LayerNorm, nn.BatchNorm2d, nn.GroupNorm)):
        nn.init.ones_(module.weight)
        nn.init.zeros_(module.bias)
